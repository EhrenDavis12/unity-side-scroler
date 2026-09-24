#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// An ordered JSON object: a plain Dictionary doesn't guarantee insertion order is preserved
/// (only that it happens to work today, absent any removal), and CharacterImporter relies on
/// the JSON's own key order for the controller's state order and the action-button order — so
/// this keeps an explicit ordered list of entries instead.
/// </summary>
public sealed class JsonObject : IEnumerable<KeyValuePair<string, object>>
{
    private readonly List<KeyValuePair<string, object>> _entries = new List<KeyValuePair<string, object>>();

    public void Add(string key, object value) => _entries.Add(new KeyValuePair<string, object>(key, value));

    public IEnumerable<string> Keys
    {
        get
        {
            foreach (KeyValuePair<string, object> entry in _entries) yield return entry.Key;
        }
    }

    public object this[string key]
    {
        get
        {
            if (TryGetValue(key, out object value)) return value;
            throw new KeyNotFoundException(key);
        }
    }

    public bool TryGetValue(string key, out object value)
    {
        foreach (KeyValuePair<string, object> entry in _entries)
        {
            if (entry.Key == key)
            {
                value = entry.Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    public bool ContainsKey(string key) => TryGetValue(key, out _);

    public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// A minimal recursive-descent JSON reader, just enough to parse
/// Assets/Characters/characters.json for CharacterImporter: objects (as <see cref="JsonObject"/>,
/// preserving source key order), arrays, strings, numbers (as double), booleans and null.
/// </summary>
public static class MiniJson
{
    public static object Deserialize(string json)
    {
        int index = 0;
        object result = ParseValue(json, ref index);
        return result;
    }

    private static object ParseValue(string json, ref int index)
    {
        SkipWhitespace(json, ref index);
        char c = json[index];
        switch (c)
        {
            case '{': return ParseObject(json, ref index);
            case '[': return ParseArray(json, ref index);
            case '"': return ParseString(json, ref index);
            case 't':
                index += 4; // true
                return true;
            case 'f':
                index += 5; // false
                return false;
            case 'n':
                index += 4; // null
                return null;
            default: return ParseNumber(json, ref index);
        }
    }

    private static JsonObject ParseObject(string json, ref int index)
    {
        var obj = new JsonObject();
        index++; // {
        SkipWhitespace(json, ref index);
        if (json[index] == '}')
        {
            index++;
            return obj;
        }
        while (true)
        {
            SkipWhitespace(json, ref index);
            string key = ParseString(json, ref index);
            SkipWhitespace(json, ref index);
            index++; // :
            object value = ParseValue(json, ref index);
            obj.Add(key, value);
            SkipWhitespace(json, ref index);
            if (json[index] == ',')
            {
                index++;
                continue;
            }
            index++; // }
            break;
        }
        return obj;
    }

    private static List<object> ParseArray(string json, ref int index)
    {
        var list = new List<object>();
        index++; // [
        SkipWhitespace(json, ref index);
        if (json[index] == ']')
        {
            index++;
            return list;
        }
        while (true)
        {
            object value = ParseValue(json, ref index);
            list.Add(value);
            SkipWhitespace(json, ref index);
            if (json[index] == ',')
            {
                index++;
                continue;
            }
            index++; // ]
            break;
        }
        return list;
    }

    private static string ParseString(string json, ref int index)
    {
        var sb = new StringBuilder();
        index++; // opening quote
        while (json[index] != '"')
        {
            char c = json[index];
            if (c == '\\')
            {
                index++;
                char esc = json[index];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        string hex = json.Substring(index + 1, 4);
                        sb.Append((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        index += 4;
                        break;
                }
                index++;
            }
            else
            {
                sb.Append(c);
                index++;
            }
        }
        index++; // closing quote
        return sb.ToString();
    }

    private static double ParseNumber(string json, ref int index)
    {
        int start = index;
        while (index < json.Length && "-+.eE0123456789".IndexOf(json[index]) >= 0) index++;
        return double.Parse(json.Substring(start, index - start), CultureInfo.InvariantCulture);
    }

    private static void SkipWhitespace(string json, ref int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
    }
}
#endif
