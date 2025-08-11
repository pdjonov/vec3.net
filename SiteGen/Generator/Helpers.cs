using System.IO;

namespace Vec3.Site.Generator;

internal static class Helpers
{
	public static Stream CreateFileInMaybeMissingDirectory(string path)
	{
		try
		{
			return File.Create(path);
		}
		catch(DirectoryNotFoundException)
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
		catch(DirectoryNotFoundException)
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
		catch(IOException)
		{
		}
	}
}