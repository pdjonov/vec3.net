using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using Microsoft.Extensions.FileSystemGlobbing;

namespace Vec3.Site.Generator;

internal static class Helpers
{
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

	public static string GetHashString(string data)
	{
		ArgumentNullException.ThrowIfNull(data);

		Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
		SHA256.HashData(
			source: MemoryMarshal.AsBytes(data.AsSpan()),
			destination: hash);

		return Convert.ToHexString(hash);
	}

	public static Matcher CreateMatcher(IEnumerable<string>? include, IEnumerable<string>? exclude = null, bool mustBeAbsolute = true)
	{
		if (mustBeAbsolute)
		{
			if (include != null && !include.Any(i => i.StartsWith('/')))
				throw new ArgumentException(paramName: nameof(include), message: "Patterns must be absolute.");
			if (exclude != null && !exclude.Any(i => i.StartsWith('/')))
				throw new ArgumentException(paramName: nameof(exclude), message: "Patterns must be absolute.");
		}

		var ret = new Matcher(StringComparison.Ordinal);

		if (include != null)
			foreach (var p in include)
				ret.AddInclude(p.TrimStart('/'));

		if (exclude != null)
			foreach (var p in exclude)
				ret.AddExclude(p.TrimStart('/'));

		return ret;
	}

	public static Matcher CreateMatcher(string? include, string? exclude = null, bool mustBeAbsolute = true)
	{
		return CreateMatcher(
			include: include != null ? [include] : null,
			exclude: exclude != null ? [exclude] : null,
			mustBeAbsolute: mustBeAbsolute);
	}
}