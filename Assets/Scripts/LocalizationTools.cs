using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

using SimpleFileBrowser;


public class LocalizationTools : MonoBehaviour
{
	private const string VERSION = "1.0";

	private const string CORE_PATH = "\\My Games\\CORE\\Saved\\Maps";
	private const string DEFAULT_FILE_NAME = "LocalizationTexts.lua";
	private const string KEY_LAST_SAVED_FOLDER_PATH = "LastFolderPath";
	private const string KEY_LAST_SAVED_FILE_NAME = "LastFileName";
	private const string KEY_COLUMNS_TO_IGNORE = "ColumnsToIgnore";
	private const string KEY_REVEAL_IN_EXPLORER = "RevealInExplorer";

	private LocData locData;

	private string inputColumnsToIgnore = "c, d";
	private bool revealInExplorer = true;
	private Vector2 logScrollPosition;
	private GUIStyle warningStyle = new GUIStyle();
	private GUIStyle errorStyle = new GUIStyle();
	private bool fileSelectionOpen;
	
	private enum LogType
	{
		Normal,
		Warning,
		Error,
	}
	private List<string> logMessages = new List<string>();
	private List<LogType> logTypes = new List<LogType>();


	private void Awake()
	{
		inputColumnsToIgnore = PlayerPrefs.GetString(KEY_COLUMNS_TO_IGNORE, inputColumnsToIgnore);
		revealInExplorer = (PlayerPrefs.GetInt(KEY_REVEAL_IN_EXPLORER, revealInExplorer ? 1 : 0) == 1) ? true : false;

		warningStyle.normal.textColor = Color.yellow;
		errorStyle.normal.textColor = Color.red;

		Log("Loc Import Tool");
		Log("v" + VERSION);
		Log("by: standardcombo");
		Log("");
		Log("Converts localization data from a spreadsheet into a Lua file.");
		LogUsage();
		Log("Columns to ignore: Localization sheets often have supporting columns, such as max character count");
		Log("and description. You don't want to import those as if they were languages.");
	}
	private void LogUsage()
	{
		Log("");
		Log("Usage:");
		Log(" 1. Select all content in a spreadsheet and copy it (Ctrl + A, Ctrl + C).");
		Log(" 2. Press 'Import from clipboard' above.");
		Log(" 3. Select the file location and name.");
		Log("");
	}

	private void OnGUI()
	{
		if (fileSelectionOpen) return;

		int y = 20;

		GUI.Label(new Rect(20, y, 150, 20), "Columns to ignore: ");
		inputColumnsToIgnore = GUI.TextField(new Rect(135, y, 85, 20), inputColumnsToIgnore);

		y += 30;
		revealInExplorer = GUI.Toggle(new Rect(19, y, 200, 30), revealInExplorer, " Reveal in explorer when done");

		y += 40;
		if (GUI.Button(new Rect(19, y, 201, 30), "Import from clipboard"))
		{
			ClearLog();

			locData = ParseFromClipboard(inputColumnsToIgnore);

			if (locData == null)
			{
				LogUsage();
				return;
			}

			string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + CORE_PATH;
			folderPath = PlayerPrefs.GetString(KEY_LAST_SAVED_FOLDER_PATH, folderPath);

			string fileName = PlayerPrefs.GetString(KEY_LAST_SAVED_FILE_NAME, DEFAULT_FILE_NAME);

			FileBrowser.ShowSaveDialog(OnSuccess, OnCancel, FileBrowser.PickMode.Files, 
				false, folderPath, fileName, "Save Localization Texts", "Save");

			fileSelectionOpen = true;
		}

		y += 50;
		int lineDistance = 18;
		GUI.Box(new Rect(20, y, 620, 300), "");
		logScrollPosition = GUI.BeginScrollView(new Rect(20, y, 620, 300), logScrollPosition, new Rect(0, 0, 600, logMessages.Count * lineDistance));
		
		for (int i = 0; i < logMessages.Count; i++)
		{
			if (logTypes[i] == LogType.Normal)
			{
				GUI.Label(new Rect(5, 2 + i * lineDistance, 600, 22), logMessages[i]);
			}
			else if (logTypes[i] == LogType.Warning)
			{
				GUI.Label(new Rect(5, 2 + i * lineDistance, 600, 22), logMessages[i], warningStyle);
			}
			else {
				GUI.Label(new Rect(5, 2 + i * lineDistance, 600, 22), logMessages[i], errorStyle);
			}
		}
		GUI.EndScrollView();
	}
	private void OnSuccess(string[] paths)
	{
		fileSelectionOpen = false;

		string path = paths[0];

		// Save path and file name, so it's the same next time
		int indexOfLastSlash = path.LastIndexOf('\\');
		string folderPath = path.Substring(0, indexOfLastSlash);
		string fileName = path.Substring(indexOfLastSlash + 1);
		PlayerPrefs.SetString(KEY_LAST_SAVED_FOLDER_PATH, folderPath);
		PlayerPrefs.SetString(KEY_LAST_SAVED_FILE_NAME, fileName);
		PlayerPrefs.SetString(KEY_COLUMNS_TO_IGNORE, inputColumnsToIgnore);
		PlayerPrefs.SetInt(KEY_REVEAL_IN_EXPLORER, revealInExplorer ? 1 : 0);

		// Save file
		SaveToFile(locData, path);

		string languageCodes = string.Join(",", locData.languages);
		Log("Saved texts for languages: " + languageCodes);
		Log("Location: " + path);

		// Reveal folder in explorer
#if UNITY_STANDALONE_WIN
		if (revealInExplorer && System.IO.File.Exists(path))
		{
			path = System.IO.Path.GetFullPath(path);
			System.Diagnostics.Process.Start("explorer.exe", string.Format("/select,\"{0}\"", path));
			return;
		}
#endif

		locData = null;
	}
	private void OnCancel()
	{
		fileSelectionOpen = false;
		Log("Cancelled.");
	}

	private class LocData
	{
		public List<string> TIDs = new List<string>();
		public List<string> languages = new List<string>();
		public Dictionary<string, List<string>> texts = new Dictionary<string, List<string>>();
	}

	private LocData ParseFromClipboard(string columnsToIgnoreStr)
	{
		string strFromClipboard = GUIUtility.systemCopyBuffer;
		if (string.IsNullOrEmpty(strFromClipboard))
		{
			LogError("Clipboard is empty.");
			return null;
		}
		string[] lines = strFromClipboard.Split(new char[] { '\n' });
		if (lines.Length < 3)
		{
			LogError("Invalid localization data in clipboard.");
			return null;
		}

		List<int> columnsToIgnore = ParseColumnsToIgnore(columnsToIgnoreStr);

		// Convert clipboard text into columns of text
		List<List<string>> columns = new List<List<string>>();
		int columnCount = 0;
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i];
			if (line == "\r")
			{
				line = "";
			}
			else if (line.EndsWith("\r"))
			{
				line = line.Substring(0, line.Length - 1);
			}

			string[] entries = line.Split(new char[] { '\t' });
			if (entries.Length <= 0)
			{
				LogError("Invalid localization data in clipboard.");
				return null;
			}
			if (entries.Length == 1)
			{
				LogError("You must copy the TID column along with any language columns. Copy the entire sheet, including the headers.");
				return null;
			}
					
			if (columnCount <= 0)
			{
				columnCount = entries.Length;
			}
			else if (columnCount != entries.Length)
			{
				LogError("Mismatching columns. Expected " + columnCount + " columns, but found " + entries.Length + " in line " + line);
			}
			int c = 0;
			for (int e = 0; e < entries.Length; e++)
			{
				if (columnsToIgnore.Contains(e)) continue;

				if (columns.Count <= c)
				{
					columns.Add(new List<string>());
				}
				columns[c].Add(entries[e]);
				c++;
			}
		}

		// Handle some error cases. Find TIDs column
		List<string> TIDs = columns[0];
		if (TIDs[0].StartsWith("tid_"))
		{
			LogError("You must copy the entire sheet, including the headers.");
			return null;
		}
		if (TIDs.Count < 2 || !TIDs[1].StartsWith("tid_") )
		{
			LogError("Could not find Text IDs (TIDs). It should be the first column.");
			return null;
		}
		TIDs.RemoveAt(0); // Remove header

		// Build localization data
		LocData data = new LocData();
		data.TIDs = TIDs;

		bool blankHeader = false;
		for (int i = 1; i < columns.Count; i++)
		{
			List<string> col = columns[i];
			string languageId = col[0];

			if (string.IsNullOrEmpty(languageId))
			{
				if (blankHeader) break;
				blankHeader = true;
			}
			else if (data.texts.ContainsKey(languageId))
			{
				LogWarning("Duplicate language column " + languageId + ". Ignoring the second one.");
			}
			else
			{
				col.RemoveAt(0); // Remove header

				data.languages.Add(languageId);
				data.texts[languageId] = col;
			}
		}

		return data;
	}

	private void SaveToFile(LocData data, string filePath)
	{
		StreamWriter writer = new StreamWriter(filePath, false);

		writer.WriteLine("local TEXTS = {}");

		for (int i = 0; i < data.languages.Count; i++)
		{
			string languageId = data.languages[i];
			writer.WriteLine("");
			writer.WriteLine("-- " + CodeToLanguageName(languageId));
			writer.WriteLine("TEXTS[\"" + languageId + "\"] = {");

			List<string> texts = data.texts[languageId];
			string lastTID = null;
			int blankEntries = 0;
			for (int t = 0; t < data.TIDs.Count; t++)
			{
				string tid = data.TIDs[t];

				if (string.IsNullOrEmpty(tid)) continue;

				// Add blank space to improve readability
				if (lastTID != null)
				{
					for (int n = 4; n < tid.Length && n < lastTID.Length; n++)
					{
						if (tid[n] == '_') break;

						if (tid[n] != lastTID[n])
						{
							writer.WriteLine("");
							break;
						}
					}
				}
				lastTID = tid;

				// Write the text
				string str = texts[t];
				if (str.Length == 0)
				{
					blankEntries++;
				}
				writer.WriteLine(tid + " = \"" + str + "\"");
			}

			if (blankEntries > 0)
			{
				LogWarning("Language " + languageId + " has " + blankEntries + " blank entries.");
			}

			writer.WriteLine("}");
		}

		writer.WriteLine("");
		writer.WriteLine("return TEXTS");
		writer.Close();
	}

	private List<int> ParseColumnsToIgnore(string columnsToIgnoreStr)
	{
		List<int> columnsToIgnore = new List<int>();

		string[] split = columnsToIgnoreStr.Split(new char[] { ',' });
		foreach (string column in split)
		{
			string str = column.Trim();
			if (string.IsNullOrEmpty(str)) continue;

			str = str.ToLowerInvariant();

			char c = str[0];
			if (c < 'a' || c > 'z') continue;
			
			columnsToIgnore.Add( (int)c - (int)'a' );
		}
		return columnsToIgnore;
	}

	private string CodeToLanguageName(string languageId)
	{
		languageId = languageId.ToUpperInvariant();

		if (languageId == "EN") return "English";
		if (languageId == "PT") return "Portuguese (Portugal)";
		if (languageId == "PT-BR") return "Portuguese (Brazil)";
		if (languageId == "ZH-CN") return "Chinese (Simplified)";
		if (languageId == "ZH-TW") return "Chinese (Traditional)";
		if (languageId == "FR") return "French";
		if (languageId == "DE") return "German";
		if (languageId == "RU") return "Russian";
		if (languageId == "ES-EU") return "Spanish (Spain)";
		if (languageId == "ES-LA") return "Spanish (Latin-America)";
		if (languageId == "JP") return "Japanese";
		if (languageId == "KR") return "Korean";
		if (languageId == "TK") return "Turkish";
		if (languageId == "IT") return "Italian";

		return languageId;
	}

	private void ClearLog()
	{
		logMessages.Clear();
		logTypes.Clear();
	}

	private void Log(string message)
	{
		Debug.Log(message);
		logMessages.Add(message);
		logTypes.Add(LogType.Normal);
	}

	private void LogWarning(string message)
	{
		Debug.LogWarning(message);
		logMessages.Add(message);
		logTypes.Add(LogType.Warning);
	}

	private void LogError(string message)
	{
		Debug.LogError(message);
		logMessages.Add(message);
		logTypes.Add(LogType.Error);
	}

	/*static void ExportGlyphs()
	{
		Debug.Log("Exporting unique glyphs to clipboard...");

		LocFile locFile = LoadSelection(false);
		if (locFile != null)
		{
			locFile.ExportUniqueGlyphs();
		}
	}*/
	
	/*
	private static void ImportFromClipboard(string filePath)
	{
		Debug.Log("Importing texts from clipboard...");

		string strFromClipboard = GUIUtility.systemCopyBuffer;
		if (!string.IsNullOrEmpty(strFromClipboard))
		{
			string[] lines = strFromClipboard.Split(new char[] { '\n' });
			if (lines.Length < 3)
			{
				Debug.LogError("Invalid localization data in clipboard.");
			}
			else
			{
				List<List<string>> columns = new List<List<string>>();
				int columnCount = 0;
				for (int i = 0; i < lines.Length; i++)
				{
					string line = lines[i];
					if (line == "\r")
					{
						line = "";
					}
					else if (line.EndsWith("\r"))
					{
						line = line.Substring(0, line.Length - 1);
					}

					string[] entries = line.Split(new char[] { '\t' });
					if (entries.Length <= 0)
					{
						Debug.LogError("Invalid localization data in clipboard.");
						return;
					}
					if (entries.Length == 1)
					{
						Debug.LogError("You must copy the TID column along with any language columns. Copy the entire columns, including the headers.");
						return;
					}
					
					if (columnCount <= 0)
					{
						columnCount = entries.Length;
					}
					else if (columnCount != entries.Length)
					{
						Debug.LogError("Mismatching columns. Expected " + columnCount + " columns, but found " + entries.Length + " in line " + line);
					}
					for (int e = 0; e < entries.Length; e++)
					{
						if (columns.Count <= e)
						{
							columns.Add(new List<string>());
						}
						columns[e].Add(entries[e]);
					}
				}

				List<string> TIDs = columns[0];
				if (TIDs[0].StartsWith("tid_") || TIDs[1].StartsWith("tid_"))
				{
					Debug.LogError("You must copy the entire columns, including the headers.");
					return;
				}
				if (TIDs.Count < 3 || !TIDs[2].StartsWith("tid_") )
				{
					Debug.LogError("TID column must be copied as well");
					return;
				}

				bool blankHeader = false;
				for (int i = 1; i < columns.Count; i++)
				{
					if (string.IsNullOrEmpty(columns[i][0]))
					{
						if (blankHeader) break;
						blankHeader = true;
					}
					else {
						ImportLanguage(TIDs, columns[i], filePath);
					}
				}

				AssetDatabase.Refresh(ImportAssetOptions.Default);
			}
		}
	}
	*/
	
	/*
	static void ImportLanguage(List<string> TIDs, List<string> textColumn, string filePath)
	{
		string sheetName = TIDs[0];
		if (sheetName != textColumn[0])
		{
			Debug.LogError("Error importing column. Sheet name '" + sheetName + "' from TIDs does not match sheet '" + textColumn[0] + "' from the texts column.");
			return;
		}

		string languageId = textColumn[1];

		//string filePath = "Assets/Resources/Texts/" + languageId + "_" + sheetName + ".txt";
		filePath += "/" + languageId + "_" + sheetName + ".txt";

		if (TIDs.Count != textColumn.Count)
		{
			Debug.LogError("TIDs(" + TIDs.Count + ") and texts(" + textColumn.Count + ") have a mismatching number of elements in " + languageId);
			return;
		}

		Debug.Log("Importing the " + sheetName + " sheet in " + languageId);

		int blankEntries = 0;

		Debug.Log("filePath = " + filePath);

		StreamWriter writer = new StreamWriter(filePath, false);

		// Write header
		writer.WriteLine("{");
		writer.WriteLine(string.Format("id:{0},", languageId));
		writer.WriteLine(string.Format("sheet:{0},", sheetName));
		writer.WriteLine("texts:[");

		// Write lines
		bool blankRow = false;
		string lastTID = null;
		for (int i = 2; i < TIDs.Count; i++)
		{
			string tid = TIDs[i];
			string text = textColumn[i];

			if (string.IsNullOrEmpty(tid))
			{
				if (blankRow) break;
				blankRow = true;
				continue;
			}
			blankRow = false;

			if (lastTID != null)
			{
				for (int t = 4; t < tid.Length && t < lastTID.Length; t++)
				{
					if (tid[t] == '_') break;

					if (tid[t] != lastTID[t])
					{
						writer.WriteLine("");
						break;
					}
				}
			}

			// Eliminate | formatting bars
			if (text.Length >= 2)
			{
				bool trimStart = (text[0] == '|');
				bool trimEnd = (text[text.Length - 1] == '|');
				if (trimStart || trimEnd)
				{
					int startIndex = trimStart ? 1 : 0;
					int length = text.Length;
					if (trimStart) length--;
					if (trimEnd) length--;

					text = text.Substring(startIndex, length);
					
					// Validate metadata |
					if (trimStart != trimEnd)
					{
						Debug.LogError("Formatting bars | not consistent on " + tid + " in " + sheetName + ":" + languageId);
					}
				}
			}

			// Validate metadata [color]
			int indexOf = -1;
			do
			{
				indexOf = text.IndexOf("[color=", indexOf + 1);
				if (indexOf >= 0)
				{
					int closingIndex = text.IndexOf("[/color]", indexOf + 1);
					if (closingIndex < 0)
					{
						Debug.LogError("No closing [/color] on " + tid + " in " + sheetName + ":" + languageId);
					}
				}
			}
			while (indexOf > 0);

			if (text.Length == 0)
			{
				blankEntries++;

				if (blankEntries <= 5)
				{
					string warningMessage = tid + " is empty in " + sheetName + ":" + languageId;
					if (blankEntries == 5)
					{
						warningMessage += ". Supressing further warning of this type...";
					}
					Debug.LogWarning(warningMessage);
				}
				text = "#";
			}
			
			text = text.Replace('\"', '＂');

			writer.WriteLine("	" + tid + ", \"" + text + "\",");

			lastTID = tid;
		}

		// Write footer
		writer.WriteLine("],");
		writer.WriteLine("}");

		writer.Close();

		if (blankEntries > 0)
		{
			Debug.LogWarning("WARNING: " + languageId + " has " + blankEntries + " empty lines in " + sheetName + " sheet.");
		}
	}
	*/
	
	/*
	private static LocFile LoadSelection(bool enforceSameSheet = true)
	{
		if (Selection.objects.Length > 0 && Selection.objects[0] is TextAsset)
		{
			LocFile locFile = null;

			for (int i = 0; i < Selection.objects.Length; i++)
			{
				Debug.Log("Processing " + Selection.objects[i].name);

				TextAsset asset = (TextAsset)Selection.objects[i];
				if (asset != null)
				{
					string sjson = asset.text;
					Localization.File file = Localization.File.FromString(sjson);
					if (locFile == null)
					{
						locFile = new LocFile(file.sheet);
					}
					locFile.AddLocalizationFile(file, enforceSameSheet);
				}
				else
				{
					Debug.LogError("Selected file " + Selection.objects[i].name + " is not a text asset.");
				}
			}
			return locFile;
		}
		Debug.LogError("Select a text asset with localization data and try again.");
		return null;
	}
	
	private class LocFile
	{
		public string name;
		public List<string> languages = new List<string>();
		public List<string> tids = new List<string>();
		public Dictionary<string,List<string>> allRows = new Dictionary<string, List<string>>();

		public LocFile(string fileName)
		{
			name = fileName;
		}

		public void AddLocalizationFile(Localization.File inFile, bool enforceSameSheet = true)
		{
			if (enforceSameSheet && inFile.sheet != name)
			{
				Debug.LogError("Did not add file " + inFile.displayName + ":" + inFile.sheet + " because the active sheet is " + name + " and only one sheet can be processed at a time.");
				return;
			}

			string languageId = inFile.id;

			int blankEntries = languages.Count;

			languages.Add(languageId);


			bool[] tidsVisited = new bool[tids.Count];

			for (int i = 0; i < inFile.texts.Length - 1; i++)
			{
				string tid = inFile.texts[i];

				if (tid.StartsWith("tid_"))
				{
					string str = inFile.texts[i + 1];
					i++;

					if (allRows.ContainsKey(tid))
					{
						int indexOf = tids.IndexOf(tid);
						tidsVisited[indexOf] = true;

						List<string> row = allRows[tid];
						row.Add(str);
					}
					else
					{
						List<string> row = new List<string>(1);

						tids.Add(tid);
						allRows.Add(tid, row);

						for (int b = 0; b < blankEntries; b++)
						{
							row.Add("");
						}
						row.Add(str);
					}
				}
			}

			for (int i = 0; i < tidsVisited.Length; i++)
			{
				if ( !tidsVisited[i] )
				{
					string tid = tids[i];
					List<string> row = allRows[tid];
					row.Add("");
				}
			}
		}

		public void ExportTIDs()
		{
			StringBuilder sb = new StringBuilder();

			sb.Append(name);
			sb.Append('\n');

			sb.Append("TIDs");
			sb.Append('\n');
			for (int i = 0; i < tids.Count; i++)
			{
				sb.Append(tids[i]);
				sb.Append('\n');
			}

			GUIUtility.systemCopyBuffer = sb.ToString();
		}

		public void ExportTexts()
		{
			StringBuilder sb = new StringBuilder();

			for (int i = 0; i < languages.Count; i++)
			{
				sb.Append(name);
				if (i < languages.Count - 1)
				{
					sb.Append('\t');
				}
			}
			sb.Append('\n');

			for (int i = 0; i < languages.Count; i++)
			{
				sb.Append(languages[i]);
				if (i < languages.Count - 1)
				{
					sb.Append('\t');
				}
			}
			sb.Append('\n');

			for (int i = 0; i < tids.Count; i++)
			{
				string tid = tids[i];
				List<string> row = allRows[tid];
				for (int r = 0; r < row.Count; r++)
				{
					string str = row[r];

					if (str.Length > 0 && (str.Contains(",") || str[0] == ' ' || str[str.Length - 1] == ' ' || str[0] == '=' || str[0] == '+' || str[0] == '-'))
					{
						sb.Append('|');
						sb.Append(str);
						sb.Append('|');
					}
					else
					{
						sb.Append(str);
					}

					if (r < row.Count - 1)
					{
						sb.Append('\t');
					}
				}
				sb.Append('\n');
			}

			GUIUtility.systemCopyBuffer = sb.ToString();
		}

		public void ExportUniqueGlyphs()
		{
			StringBuilder sb = new StringBuilder();

			HashSet<char> allGlyphs = new HashSet<char>();

			for (int i = 0; i < tids.Count; i++)
			{
				string tid = tids[i];
				List<string> row = allRows[tid];
				for (int r = 0; r < row.Count; r++)
				{
					string str = row[r];

					for (int s = 0; s < str.Length; s++)
					{
						char c = str[s];
						allGlyphs.Add(c);
					}
				}
			}

			foreach (char c in allGlyphs)
			{
				int mappedVal = SpecialSymbols.Map(c);
				if (mappedVal < 0)
				{
					sb.Append(c);
				}
			}

			GUIUtility.systemCopyBuffer = sb.ToString();

            Debug.Log(allGlyphs.Count + " unique glyphs copied to the clipboard.");
		}
	}

	static string LoadFile(string languageName)
	{
		StreamReader reader = new StreamReader(FOLDER_PATH + languageName + EXTENSION);
		string result = reader.ReadToEnd();
		reader.Close();
		return result;
	}
	*/
}
