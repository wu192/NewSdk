using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

namespace BinaryAssetBuilder.Core;

public class FileNameXmlResolver : XmlUrlResolver
{
	private string _BaseDirectory;

	private IList<PathMapItem> _Paths = new List2<PathMapItem>();

	public IList<PathMapItem> Paths => _Paths;

	public FileNameXmlResolver(string baseDirectory)
	{
		_BaseDirectory = baseDirectory;
	}

	[DllImport("shlwapi.dll", EntryPoint = "PathRelativePathToW", CharSet = CharSet.Unicode, ExactSpelling = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool PathRelativePathTo([Out] StringBuilder result, string from, uint typeFrom, string to, uint typeTo);

	public override object GetEntity(Uri absoluteUri, string role, Type ofObjectToReturn)
	{
		string empty = string.Empty;
		string sourceUri = absoluteUri.OriginalString;
		if (absoluteUri.Scheme == "data" || absoluteUri.Scheme == "art" || absoluteUri.Scheme == "audio")
		{
			empty = FileNameResolver.ResolvePath(_BaseDirectory, absoluteUri.OriginalString);
			// This is a resolved Windows filename, not an already escaped URI.
			// Adding file:// would interpret literal # and % sequences in any
			// directory name as URI fragments/escapes and open a different file.
			absoluteUri = new Uri(empty, UriKind.Absolute);
		}
		else
		{
			if (!(absoluteUri.Scheme == "file"))
			{
				throw new BinaryAssetBuilderException(ErrorCode.IllegalPath, "Illegal URI scheme used in XInclude statement: {0}", absoluteUri.OriginalString);
			}
			// PathRelativePathToW requires a MAX_PATH output buffer, including its terminator.
			StringBuilder stringBuilder = new StringBuilder(260);
			if (!PathRelativePathTo(stringBuilder, _BaseDirectory, 16u, absoluteUri.LocalPath, 128u))
			{
				throw new BinaryAssetBuilderException(ErrorCode.IllegalPath, "Illegal absolute path used in XInclude statement: {0}", absoluteUri.AbsolutePath);
			}
			sourceUri = stringBuilder.ToString();
			empty = absoluteUri.LocalPath;
		}
		_Paths.Add(new PathMapItem(sourceUri, empty));
		return base.GetEntity(absoluteUri, role, ofObjectToReturn);
	}
}
