﻿using Newtonsoft.Json;
using PCRE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Upgrade
{
	class Entry
	{
		[JsonProperty("enum")]
		public string Enum { get; set; }

		[JsonProperty("code")]
		public string Code { get; set; }

		[JsonIgnore()]
		public Dictionary<string, int> EnumValues
		{
			get
			{
				// This is simple because we control the input so can be strict.
				string all = Enum.Split('{')[1].Split('}')[0];
				IEnumerable<string> entries = all.Split(',').Select((v) => v.Trim());

				int idx = 0;
				Dictionary<string, int> ret = new Dictionary<string, int>();
				foreach (var entry in entries)
				{
					var parts = entry.Split('=');
					if (parts.Length == 1)
					{
					}
					else if (int.TryParse(parts[1].Trim(), out int nv))
					{
						idx = nv;
					}
					else
					{
						idx = ret[parts[1].Trim()];
					}
					ret.Add(parts[0].Trim(), idx);
					idx = idx + 1;
				}
				return ret;
			}
		}

		[JsonIgnore()]
		public string TagName
		{
			get
			{
				// This is simple because we control the input so can be strict.
				return Enum.Split(':')[0].Substring(5);
			}
		}

		// Get the function name we are putting the custom tag in to.
		[JsonIgnore()]
		public string FunctionName
		{
			get
			{
				// This regex is simple because we control the input so can be strict.
				var regex = new PcreRegex("(?:native|stock) (?:\\w+\\:)?(\\w+)");
				return regex.Match(Code).Groups[1];
			}
		}

		// Does this declaration have a return tag?
		[JsonIgnore()]
		public string ReturnTag
		{
			get
			{
				// This regex is simple because we control the input so can be strict.
				var regex = new PcreRegex("(?:native|stock) (\\w+\\:)?");
				string ret = regex.Match(Code).Groups[1].ToString().Trim();
				return ret == "" ? null : ret.Substring(0, ret.Length - 1);
			}
		}
		
		// Does this declaration have a return tag?
		[JsonIgnore()]
		public string[] Params
		{
			get
			{
				int start = Code.IndexOf('(') + 1;
				return Code.Substring(start, Code.Length - start - 1).Split(',');
			}
		}
		
		// Does this declaration have a return tag?
		[JsonIgnore()]
		public int ParamCount
		{
			get
			{
				string[] p = Params;
				if (p.Length == 1 && p[0].Trim() == "")
				{
					return 0;
				}
				return p.Length;
			}
		}
		
		// Does this declaration have a return tag?
		[JsonIgnore()]
		public bool HasReturnTag
		{
			get
			{
				return !(ReturnTag is null);
			}
		}

		// Get the parameter index we are putting the custom tag in to.
		[JsonIgnore()]
		public IEnumerable<int> ReplaceIndexes
		{
			get
			{
				// This is simple because we control the input so can be strict.
				int cur = 0;
				for (; ;)
				{
					cur = Code.IndexOf('$', cur) + 1;
					if (cur == 0)
					{
						break;
					}
					yield return Code.Substring(0, cur).Count((c) => c == ',');
				}
			}
		}

		public string GetValueName(int value)
		{
			return EnumValues.FirstOrDefault((kv) => kv.Value == value).Key;
		}

		public int GetNameValue(string name)
		{
			return EnumValues.GetValueOrDefault(name, -1);
		}
	}

	// Generate the regex required to convert old code to new code.
	class Generator
	{
		[JsonProperty("generators")]
		public Entry[] Generators { get; set; }

		private string GetDefaultValue(string param)
		{
			string[] bits = param.Split('=', 2);
			if (bits.Length == 1)
			{
				return null;
			}
			return bits[1].Trim();
		}
		
		private bool IsConst(string param)
		{
			return param.Trim().StartsWith("const ");
		}
		
		private bool IsRef(string param)
		{
			return param.Contains('&');
		}

		private int WriteEnumInput(StringBuilder sb, Entry entry, int baseIdx)
		{
			sb.Append("(?:");
			foreach (var kv in entry.EnumValues)
			{
				sb.Append('(');
				sb.Append(kv.Value);
				sb.Append(")|");
				++baseIdx;
			}
			sb.Append("((?&expression)))");
			return baseIdx + 1;
		}

		private int WriteEnumOutput(StringBuilder sb, Entry entry, int baseIdx)
		{
			foreach (var kv in entry.EnumValues)
			{
				sb.Append("${");
				sb.Append(baseIdx);
				sb.Append(":+");
				sb.Append(kv.Key);
				sb.Append(':');
				++baseIdx;
			}
			sb.Append('$');
			sb.Append(baseIdx);
			foreach (var kv in entry.EnumValues)
			{
				sb.Append('}');
			}
			return baseIdx + 1;
		}

		private void WriteUseScanner(StringBuilder sb, Entry entry)
		{
			int paramIdx = -1;
			int matchIdx = 2;
			int replaceIdx = 0;
			int[] locations = entry.ReplaceIndexes.ToArray();
			sb.Append("((?&symbol)?)");
			sb.Append(entry.FunctionName);
			sb.Append("\\\\s*\\\\(");
			for ( ; ; )
			{
				int nextIdx = locations[replaceIdx];
				int diff = nextIdx - paramIdx - 1;
				// Skip over all the intervening parameters at once.
				switch (diff)
				{
				case 0:
					break;
				case 1:
					sb.Append("((?&expression),)");
					++matchIdx;
					break;
				default:
					sb.Append("((?:(?&expression),){");
					sb.Append(diff);
					sb.Append("})");
					++matchIdx;
					break;
				}
				// Output the replacement scanner.
				sb.Append("\\\\s*");
				matchIdx = WriteEnumInput(sb, entry, matchIdx);
				sb.Append("\\\\s*");
				paramIdx = nextIdx;
				++replaceIdx;
				if (replaceIdx == locations.Length)
				{
					sb.Append("([,)])");
					break;
				}
				else
				{
					sb.Append(",");
				}
			}
		}
		
		private void WriteUseReplacer(StringBuilder sb, Entry entry)
		{
			int paramIdx = -1;
			int matchIdx = 2;
			int replaceIdx = 0;
			int paramCount = entry.ParamCount;
			int[] locations = entry.ReplaceIndexes.ToArray();
			sb.Append("$1");
			sb.Append(entry.FunctionName);
			sb.Append('(');
			for ( ; ; )
			{
				int nextIdx = locations[replaceIdx];
				int diff = nextIdx - paramIdx - 1;
				// Skip over all the intervening parameters at once.
				switch (diff)
				{
				case 0:
					break;
				default:
					sb.Append('$');
					sb.Append(matchIdx);
					++matchIdx;
					break;
				}
				if (matchIdx != 2)
				{
					sb.Append(' ');
				}
				// Output the enum replacement.
				matchIdx = WriteEnumOutput(sb, entry, matchIdx);
				paramIdx = nextIdx;
				++replaceIdx;
				if (replaceIdx == locations.Length)
				{
					sb.Append('$');
					sb.Append(matchIdx);
					break;
				}
			}
		}
		
		private void WriteDeclarationScanner(StringBuilder sb, Entry entry)
		{
			int paramIdx = 0;
			int replaceIdx = 0;
			int paramCount = entry.ParamCount;
			int[] locations = entry.ReplaceIndexes.ToArray();
			sb.Append("((?&start))((?&stocks))\\\\s+");
			string tag = entry.ReturnTag;
			if (!(tag is null))
			{
				sb.Append(tag);
				sb.Append("\\\\s*:\\\\s*");
			}
			sb.Append("((?&symbol))?");
			sb.Append(entry.FunctionName);
			sb.Append("\\\\s*\\\\(");
			while (replaceIdx < locations.Length)
			{
				if (paramIdx == locations[replaceIdx])
				{
					++replaceIdx;
					// Output the replacement scanner.
					sb.Append("\\\\s*(?:(?&tag)\\\\s*)?((?&symbol))(?:\\\\s*=\\\\s*(?&expression))?");
				}
				else
				{
					// Output the default scanner.
					sb.Append("\\\\s*((?&parameter))");
				}
				++paramIdx;
				if (paramIdx == paramCount)
				{
					// Output a `)`.
					sb.Append("\\\\s*\\\\)");
				}
				else
				{
					// Output a `,`.
					sb.Append("\\\\s*,");
				}	
			}
		}
		
		private void WriteDeclarationReplacer(StringBuilder sb, Entry entry)
		{
			int paramIdx = 0;
			int replaceIdx = 0;
			int paramCount = entry.ParamCount;
			int[] locations = entry.ReplaceIndexes.ToArray();
			string[] p = entry.Params;
			sb.Append("$1$2 ");
			string tag = entry.ReturnTag;
			if (!(tag is null))
			{
				sb.Append(tag);
				sb.Append(":");
			}
			tag = entry.TagName;
			sb.Append("$3");
			sb.Append(entry.FunctionName);
			sb.Append("(");
			while (replaceIdx < locations.Length)
			{
				if (paramIdx != 0)
				{
					sb.Append(" ");
				}
				if (paramIdx == locations[replaceIdx])
				{
					++replaceIdx;
					// Output the replacement scanner.
					if (IsConst(p[paramIdx]))
					{
						sb.Append("const ");
					}
					if (IsRef(p[paramIdx]))
					{
						sb.Append("&");
					}
					sb.Append(tag);
					sb.Append(":$");
					sb.Append(paramIdx + 4);
					string def = GetDefaultValue(p[paramIdx]);
					if (!(def is null))
					{
						sb.Append(" = ");
						sb.Append(def);
					}
				}
				else
				{
					// Output the default scanner.
					sb.Append("$");
					sb.Append(paramIdx + 4);
				}
				++paramIdx;
				if (paramIdx == paramCount)
				{
					// Output a `)`.
					sb.Append(")");
				}
				else
				{
					// Output a `,`.
					sb.Append(",");
				}	
			}
		}

		public void Dump()
		{
			var sb = new StringBuilder("{\n\t\"defines\":\n\t{\n\t},\n\t\"replacements\":\n\t[");
			bool first = true;
			foreach (var entry in Generators)
			{
				if (first)
				{
					first = false;
				}
				else
				{
					sb.Append(',');
				}
				sb.Append("\n\t\t{\n\t\t\t\"description\": \"Add tags to `");
				sb.Append(entry.FunctionName);
				sb.Append("`\",\n\t\t\t\"from\": \"");
				WriteDeclarationScanner(sb, entry);
				sb.Append("\",\n\t\t\t\"to\": \"");
				WriteDeclarationReplacer(sb, entry);
				sb.Append("\"\n\t\t},\n\t\t{\n\t\t\t\"description\": \"Add enums to `");
				sb.Append(entry.FunctionName);
				sb.Append("`\",\n\t\t\t\"from\": \"");
				WriteUseScanner(sb, entry);
				sb.Append("\",\n\t\t\t\"to\": \"");
				WriteUseReplacer(sb, entry);
				sb.Append("\"\n\t\t}");
			}
			sb.Append("\n\t]\n}\n\n");
			System.Console.WriteLine(sb.ToString());
		}

		public Generator()
		{
		}
	}
}

