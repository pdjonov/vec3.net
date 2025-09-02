using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Vec3.Site.Generator;

internal static class Helpers
{
	public static string FullNameWithoutGenericTag(this Type type)
	{
		var ret = type.FullName!;

		var idx = ret.LastIndexOf('`');
		if (idx != -1)
		{
			var doTrim = idx + 1 < ret.Length;
			for (var i = idx + 1; i < ret.Length && doTrim; i++)
				if (!char.IsDigit(ret[i]))
					doTrim = false;

			if (doTrim)
				ret = ret.Substring(0, idx);
		}

		return ret;
	}

	public static Exception? GetException(this Task task)
	{
		if (task.IsFaulted)
		{
			return task.Exception.InnerExceptions.Count == 1 ?
				task.Exception.InnerExceptions[0] :
				task.Exception;
		}

		if (task.IsCanceled)
			return new TaskCanceledException(task);

		return null;
	}

	public static string NormalizePathSeparators(this string path)
	{
		Debug.Assert(path != null);
		return path.Replace('\\', '/');
	}

	public static string GetProjectRelativePath(string relativeTo, string path)
	{
		Debug.Assert(relativeTo != null);
		Debug.Assert(path != null);

		return '/' + Path.GetRelativePath(relativeTo, path).NormalizePathSeparators();
	}

	public static string RemoveLastPathSegment(string path)
	{
		Debug.Assert(path != null);
		Debug.Assert(!path.Contains('\\'));
		Debug.Assert(!path.EndsWith('/'));

		var lastSep = path.LastIndexOf('/');
		if (lastSep == -1)
			return "";

		if (lastSep == 0 && path.Length > 1)
			return "/";

		return path.Substring(0, lastSep);
	}

	public static void ValidateRootedPath(string path, bool mustBeNormalized = true, [CallerArgumentExpression(nameof(path))] string paramName = "path")
	{
		ValidatePathCore(path, mustBeNormalized: mustBeNormalized, mustNotEscapeRoot: true, paramName: paramName);
		if (!path.StartsWith('/'))
			throw new ArgumentException(paramName: paramName, message: "The path must be rooted.");
	}

	public static void ValidateRelativePath(string path, bool mustBeNormalized = true, bool mustNotEscapeRoot = true, [CallerArgumentExpression(nameof(path))] string paramName = "path")
	{
		ValidatePathCore(path, mustBeNormalized: mustBeNormalized, mustNotEscapeRoot: mustNotEscapeRoot, paramName: paramName);
		if (path.StartsWith('/'))
			throw new ArgumentException(paramName: paramName, message: "Path must not start with a directory separator.");
	}

	private static void ValidatePathCore(string path, bool mustBeNormalized, bool mustNotEscapeRoot, string paramName)
	{
		ArgumentNullException.ThrowIfNull(path, paramName: paramName);

		if (path.Contains('\\'))
			throw new ArgumentException(paramName: paramName, message: "Path must use forward slashes.");

		if (mustNotEscapeRoot && path.Contains(".."))
		{
			//eek! this is *hideous*

			var parts = CollapseRelativeDirectoryReferences(path);

			if (parts.Contains(".."))
				throw new ArgumentException(paramName: paramName, message: "Path must not escape its root folder.");
		}

		if (mustBeNormalized)
		{
			if (path == "." || path.StartsWith("./") || path.EndsWith("/.") || path.Contains("/./"))
				throw new ArgumentException(paramName: paramName, message: "Path must not contain dot-dirs.");
			if (path.Contains("//"))
				throw new ArgumentException(paramName: paramName, message: "Path must not contain empty segments.");
		}
	}

	private static List<string> CollapseRelativeDirectoryReferences(string path)
	{
		var parts = path.Split('/').ToList();
		for (var i = 0; i < parts.Count - 1; i++)
		{
			if (parts[i] != ".." && parts[i + 1] == "..")
				parts.RemoveRange(i--, 2);
			else if (parts[i] == ".")
				parts.RemoveAt(i--);
			else if (parts[i] == "" && i > 0)
				parts.RemoveAt(i--);
		}

		return parts;
	}

	public static string CombineContentRelativePaths(string relativeTo, string path, bool resultMustBeRooted = true)
	{
		ArgumentNullException.ThrowIfNull(relativeTo);
		if (relativeTo.Contains('\\'))
			throw new ArgumentException(paramName: nameof(relativeTo), message: "Path must use forward slashes.");
		if (resultMustBeRooted && !relativeTo.StartsWith('/'))
			throw new ArgumentException(paramName: nameof(relativeTo), message: "A rooted path can only be ensured if the base path is rooted.");

		ArgumentNullException.ThrowIfNull(path);
		if (path.Contains('\\'))
			throw new ArgumentException(paramName: nameof(path), message: "Path must use forward slashes.");

		string combined;
		if (path.StartsWith('/'))
			combined = path;
		else if (relativeTo.EndsWith('/'))
			combined = relativeTo + path;
		else
			combined = relativeTo + "/" + path;

		if (combined.Contains('.') || combined.Contains("//"))
		{
			var parts = CollapseRelativeDirectoryReferences(combined);

			if (parts.Count > 0 && resultMustBeRooted)
			{
				if (parts[0] == "")
					parts.RemoveAt(0);
				if (parts.Contains(".."))
					throw new ArgumentException(paramName: nameof(path), message: "Path must not escape its root folder.");
			}

			combined = string.Join('/', parts);

			if (resultMustBeRooted)
			{
				Debug.Assert(!combined.StartsWith('/'));
				combined = '/' + combined;
			}
		}

		Debug.Assert(!resultMustBeRooted || combined.StartsWith('/'));

		return combined;
	}

	public static Stream CreateFileInMaybeMissingDirectory(string path)
	{
		try
		{
			return File.Create(path);
		}
		catch (DirectoryNotFoundException)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			return File.Create(path);
		}
	}

	public static void WriteAllTextInMaybeMissingDirectory(string path, string contents, Encoding? encoding = null)
	{
		try
		{
			File.WriteAllText(path, contents, encoding ?? Encoding.UTF8);
		}
		catch (DirectoryNotFoundException)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, contents);
		}
	}

	public static void DeleteNoThrowIoException(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (IOException)
		{
		}
	}

	public static async Task<bool> StreamContentsEqual(Stream a, Stream b)
	{
		Debug.Assert(a != null && a.CanRead && a.Position == 0);
		Debug.Assert(b != null && b.CanRead && b.Position == 0);

		var length = a.Length;
		if (length != b.Length)
			return false;

		var bufArray = ArrayPool<byte>.Shared.Rent(minimumLength: 16 * 1024 * 2);
		var bufA = bufArray.AsMemory(0, bufArray.Length / 2);
		var bufB = bufArray.AsMemory(bufA.Length, bufA.Length);
		try
		{
			while (length > 0)
			{
				var nRead = (int)Math.Min(length, bufA.Length);

				var sliceA = bufA.Slice(0, nRead);
				var sliceB = bufB.Slice(0, nRead);

				var readA = a.ReadAsync(sliceA);
				var readB = b.ReadAsync(sliceB);

				var nA = await readA;
				var nB = await readB;

				if (nA != nB)
					throw new EndOfStreamException("This shouldn't be possible. We checked that this many bytes exist...");

				if (!sliceA.Span.SequenceEqual(sliceB.Span))
					return false;

				length -= nRead;
			}

			Debug.Assert(length == 0);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(bufArray, clearArray: false);
		}

		return true;
	}

	public static string GetHashString(string data)
	{
		ArgumentNullException.ThrowIfNull(data);

		Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
		SHA256.HashData(
			source: MemoryMarshal.AsBytes(data.AsSpan()),
			destination: hash);

		return Convert.ToHexString(hash);
	}
}
