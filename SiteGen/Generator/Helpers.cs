using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

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

	public static void ValidateRelativePath(string path, bool mustBeNormalized = true, bool mustNotEscapeRoot = true, [CallerArgumentExpression(nameof(path))] string paramName = "path")
	{
		ArgumentNullException.ThrowIfNull(path);

		if (path.Contains('\\'))
			throw new ArgumentException(paramName: paramName, message: "Path must use forward slashes.");
		if (path.StartsWith('/'))
			throw new ArgumentException(paramName: paramName, message: "Path must not start with a directory separator.");

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
		ArgumentNullException.ThrowIfNull(path);
		if (relativeTo.Contains('\\'))
			throw new ArgumentException(paramName: nameof(relativeTo), message: "Path must use forward slashes.");
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
		}
		else if (resultMustBeRooted && combined.StartsWith('/'))
		{
			combined = combined.Substring(1);
		}

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

	public static void WriteAllTextInMaybeMissingDirectory(string path, string contents)
	{
		try
		{
			File.WriteAllText(path, contents);
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
}