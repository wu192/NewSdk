using System;
using System.IO;

namespace BinaryAssetBuilder.Core;

public class FileNameResolver
{
	private static Tracer _Tracer = Tracer.GetTracer("FileNameResolver", "Resolves aliased path names specified in documents");

	private static char[] SplitCharacters = new char[1] { ':' };

	public static FileNameXmlResolver GetXmlResolver(string baseDirectory)
	{
		return new FileNameXmlResolver(baseDirectory);
	}

	private static string BuildPathProbe(string baseDirectory, string templatePath)
	{
		if (templatePath == "*")
		{
			return baseDirectory;
		}
		return Path.GetFullPath(Path.Combine(Settings.Current.DataRoot, templatePath));
	}

	public static string ResolvePath(string baseDirectory, string targetPath)
	{
		string text = null;
		if (!Path.IsPathRooted(baseDirectory))
		{
			throw new BinaryAssetBuilderException(ErrorCode.InternalError, "An illegal relative path {0} is used as base directory. This is a bug in the code and needs to be fixed.", baseDirectory);
		}
		if (Path.IsPathRooted(targetPath))
		{
			text = targetPath;
		}
		else
		{
			string[] array = targetPath.Split(SplitCharacters);
			if (array.Length == 1)
			{
				text = Path.GetFullPath(Path.Combine(baseDirectory, targetPath));
			}
			else
			{
				if (array.Length > 2)
				{
					throw new BinaryAssetBuilderException(ErrorCode.IllegalPath, "An illegal path {0} is used as a reference to another asset or document.", baseDirectory);
				}
				string text2 = array[1].Replace('/', '\\');
				string text3 = array[0].ToLower();
				switch (text3)
				{
				case "art":
					if (string.IsNullOrEmpty(text2))
					{
						throw new BinaryAssetBuilderException(ErrorCode.IllegalPath, "An art alias cannot have an empty filename: {0}.", targetPath);
					}
					if (string.IsNullOrEmpty(ShPath.RemoveFileSpec(text2)))
					{
						string[] artPaths = Settings.Current.ArtPaths;
						foreach (string text4 in artPaths)
						{
							// Only non-wildcard entries use the two-character art bucket.
							// A preceding wildcard may already have resolved a short name.
							if (text4 != "*" && text2.Length < 2)
							{
								throw new BinaryAssetBuilderException(ErrorCode.IllegalPath, "Art alias {0} is too short for the two-character bucket search in {1}.", targetPath, text4);
							}
							string path = ((text4 == "*") ? baseDirectory : Path.Combine(text4, text2.Substring(0, 2)));
							if (!string.IsNullOrEmpty(Settings.Current.Postfix))
							{
								string path2 = $"{Path.GetFileNameWithoutExtension(text2)}_{Settings.Current.Postfix}{Path.GetExtension(text2)}";
								text = Path.GetFullPath(Path.Combine(path, path2));
								if (File.Exists(text))
								{
									break;
								}
							}
							text = Path.GetFullPath(Path.Combine(path, text2));
							if (File.Exists(text))
							{
								break;
							}
						}
					}
					else
					{
						text = SearchPaths(baseDirectory, text2, Settings.Current.ArtPaths);
					}
					break;
				case "data":
					text = SearchPaths(baseDirectory, text2, Settings.Current.DataPaths);
					break;
				case "audio":
					text = SearchPaths(baseDirectory, text2, Settings.Current.AudioPaths);
					break;
				case "root":
					text = Path.GetFullPath(Path.Combine(Settings.Current.DataRoot, text2));
					break;
				default:
					throw new BinaryAssetBuilderException(ErrorCode.IllegalPathAlias, "An illegal alias {0} is used in path {1}.", text3, targetPath);
				}
			}
		}
		_Tracer.TraceData("Result: {0}", text);
		return text;
	}

	// Windows paths are case-insensitive, but textual prefixes are not directory
	// boundaries (Data must never match Data2). Keep the configured search order.
	public static string GetDataRoot(string filename)
	{
		string fullName = NormalizeAbsolutePath(filename);
		foreach (string path in Settings.Current.DataPaths)
		{
			if (path == "*")
			{
				continue;
			}
			string root = NormalizeDirectoryRoot(path);
			string prefix = DirectoryPrefix(root);
			if (fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && fullName.Length > prefix.Length)
			{
				return root;
			}
		}
		return "";
	}

	private static string NormalizeAbsolutePath(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			throw new BinaryAssetBuilderException(ErrorCode.IllegalPath, "A data root or source path cannot be null or empty.");
		}
		string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		bool drivePath = normalized.Length >= 3 && char.IsLetter(normalized[0]) && normalized[1] == ':' && normalized[2] == Path.DirectorySeparatorChar;
		bool uncPath = normalized.StartsWith(@"\\", StringComparison.Ordinal);
		if (uncPath)
		{
			string[] parts = normalized.Substring(2).Split(Path.DirectorySeparatorChar);
			uncPath = parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0;
		}
		// Device syntax has different normalization/alias rules and is outside the
		// supported Win32/MAX_PATH contract. Never resolve drive-relative or
		// root-relative inputs against mutable process state.
		if ((!drivePath && !uncPath) || normalized.StartsWith(@"\\?\", StringComparison.Ordinal) || normalized.StartsWith(@"\\.\", StringComparison.Ordinal) || normalized.IndexOf(':', drivePath ? 2 : 0) >= 0)
		{
			throw new BinaryAssetBuilderException(ErrorCode.IllegalPath, "Path {0} must be an absolute drive or UNC path without device syntax or alternate data streams.", path);
		}
		try
		{
			return Path.GetFullPath(normalized);
		}
		catch (Exception error) when (error is ArgumentException || error is NotSupportedException || error is PathTooLongException)
		{
			throw new BinaryAssetBuilderException(error, ErrorCode.IllegalPath, "Path {0} is not a supported absolute path.", path);
		}
	}

	private static string NormalizeDirectoryRoot(string root)
	{
		string fullRoot = NormalizeAbsolutePath(root);
		int minimumLength = Path.GetPathRoot(fullRoot).Length;
		while (fullRoot.Length > minimumLength && fullRoot[fullRoot.Length - 1] == Path.DirectorySeparatorChar)
		{
			fullRoot = fullRoot.Substring(0, fullRoot.Length - 1);
		}
		return fullRoot;
	}

	private static string DirectoryPrefix(string root)
	{
		return root[root.Length - 1] == Path.DirectorySeparatorChar ? root : root + Path.DirectorySeparatorChar;
	}

	// Both source logical names and output stems use the same boundary and offset.
	// In particular, drive/UNC roots already end in a separator: adding one to
	// their length would silently drop the first character of the relative path.
	public static string GetSafeRelativePath(string root, string filename)
	{
		string prefix = DirectoryPrefix(NormalizeDirectoryRoot(root));
		string fullName = NormalizeAbsolutePath(filename);
		if (!fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || fullName.Length <= prefix.Length)
		{
			throw new BinaryAssetBuilderException(ErrorCode.IllegalPath, "Path {0} is not a file below data root {1}.", filename, root);
		}
		string relative = fullName.Substring(prefix.Length);
		if (Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0)
		{
			throw new BinaryAssetBuilderException(ErrorCode.IllegalPath, "Path {0} does not produce a safe relative output path.", filename);
		}
		foreach (string component in relative.Split(Path.DirectorySeparatorChar))
		{
			if (component.Length == 0 || component == "." || component == "..")
			{
				throw new BinaryAssetBuilderException(ErrorCode.IllegalPath, "Path {0} does not produce a safe relative output path.", filename);
			}
		}
		return relative;
	}

	private static string SearchPaths(string baseDirectory, string path, string[] includePaths)
	{
		string text = null;
		if (includePaths.Length > 1)
		{
			foreach (string text2 in includePaths)
			{
				text = ShPath.Canonicalize(Path.Combine((text2 == "*") ? baseDirectory : text2, path));
				if (File.Exists(text))
				{
					return text;
				}
			}
		}
		else if (includePaths.Length > 0)
		{
			text = ShPath.Canonicalize(Path.Combine((includePaths[0] == "*") ? baseDirectory : includePaths[0], path));
		}
		return text;
	}
}
