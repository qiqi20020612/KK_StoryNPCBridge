using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        sealed class AdvTextToken
        {
            internal string Text, Tag;
            internal int Kind, End;
        }
        sealed class AdvTextPage
        {
            internal int End;
            internal string Original, Argument, Display;
        }
        static readonly Regex AdvStyleTag = new Regex(@"\G<(/?)(b|i|size|color|material)(?:=[^<>]+)?>", RegexOptions.IgnoreCase);
        static List<AdvTextToken> AdvTokens(string text)
        {
            var tokens = new List<AdvTextToken>();
            for (int i = 0; i < text.Length; )
            {
                Match tag = AdvStyleTag.Match(text, i); string element;
                if (tag.Success)
                {
                    element = tag.Value; i += element.Length;
                    tokens.Add(new AdvTextToken { Text = element, End = i, Tag = tag.Groups[2].Value.ToLowerInvariant(), Kind = tag.Groups[1].Length == 0 ? 1 : -1 });
                    continue;
                }
                element = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : StringInfo.GetNextTextElement(text, i); i += element.Length;
                // Keep joined emoji and variation selectors with their preceding element.
                while (i < text.Length && (text[i] == '\u200d' || text[i] == '\ufe0f'))
                {
                    int start = i++;
                    if (text[start] == '\u200d' && i < text.Length) i += StringInfo.GetNextTextElement(text, i).Length;
                    element += text.Substring(start, i - start);
                }
                tokens.Add(new AdvTextToken { Text = element, End = i });
            }
            return tokens;
        }
        static void AdvStyle(List<AdvTextToken> stack, AdvTextToken token)
        {
            if (token.Kind == 1) stack.Add(token);
            else if (token.Kind == -1 && stack.Count > 0 && stack[stack.Count - 1].Tag == token.Tag) stack.RemoveAt(stack.Count - 1);
        }
        static string AdvOpenStyles(List<AdvTextToken> stack)
        { var b = new StringBuilder(); foreach (var token in stack) b.Append(token.Text); return b.ToString(); }
        static string AdvCloseStyles(List<AdvTextToken> stack)
        { var b = new StringBuilder(); for (int i = stack.Count - 1; i >= 0; i--) b.Append("</" + stack[i].Tag + ">"); return b.ToString(); }
        static string AdvDisplayFallback(string text)
        {
            var b = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { b.Append('\u25a1'); i++; }
                else b.Append(text[i]);
            return b.ToString().Replace("\r\n", "\n").Replace('\r', '\n');
        }
        sealed class AdvPageLayout : IDisposable
        {
            internal readonly string Signature;
            internal readonly Dictionary<string, object> Info;
            readonly GameObject copyObject;
            readonly Text copy;
            readonly HyphenationJpn hyphenation;
            readonly TextGenerator generator = new TextGenerator();
            readonly TextGenerationSettings settings;
            readonly float width, height, pixels;
            internal AdvPageLayout(Text source)
            {
                Rect rect = source.rectTransform.rect; pixels = source.pixelsPerUnit;
                if (source.font == null || rect.width <= 2 || rect.height <= 2 || pixels <= 0)
                    throw new BridgeException("dialogue_layout_unavailable", "The native ADV text font or rectangle is not ready");
                width = rect.width - 2; height = rect.height - 2;
                Info = Json.Map("width", rect.width, "height", rect.height, "availableWidth", width, "availableHeight", height,
                    "font", source.font.name, "fontSize", source.fontSize, "lineSpacing", source.lineSpacing, "pixelsPerUnit", pixels);
                Signature = Json.Stringify(Info) + ":" + source.font.GetInstanceID() + ":" + source.fontStyle + ":" + source.supportRichText + ":" + source.resizeTextForBestFit + ":" + source.resizeTextMinSize + ":" + source.resizeTextMaxSize;
                copyObject = new GameObject("KK_StoryNPCBridge.Layout", typeof(RectTransform));
                copyObject.hideFlags = HideFlags.HideAndDontSave; copyObject.SetActive(false);
                copyObject.transform.SetParent(source.transform.parent, false);
                copy = copyObject.AddComponent<Text>(); copy.font = source.font; copy.fontSize = source.fontSize;
                copy.fontStyle = source.fontStyle; copy.lineSpacing = source.lineSpacing; copy.supportRichText = source.supportRichText;
                copy.alignment = source.alignment; copy.resizeTextForBestFit = source.resizeTextForBestFit;
                copy.resizeTextMinSize = source.resizeTextMinSize; copy.resizeTextMaxSize = source.resizeTextMaxSize;
                copy.rectTransform.sizeDelta = rect.size;
                hyphenation = copyObject.AddComponent<HyphenationJpn>();
                AccessTools.Field(typeof(HyphenationJpn), "_text").SetValue(hyphenation, copy);
                settings = source.GetGenerationSettings(rect.size);
                settings.horizontalOverflow = HorizontalWrapMode.Overflow; settings.verticalOverflow = VerticalWrapMode.Overflow;
            }
            string NativeFormat(string text)
            { return (string)AccessTools.Method(typeof(HyphenationJpn), "GetFormatedText", new Type[] { typeof(Text), typeof(string) }, null).Invoke(hyphenation, new object[] { copy, text }); }
            float Width(string text) { return generator.GetPreferredWidth(text, settings) / pixels; }
            float Height(string text) { return generator.GetPreferredHeight(text, settings) / pixels; }
            string RepairWideLines(string formatted)
            {
                if (Width(formatted) <= width) return formatted;
                var output = new StringBuilder(); var line = new StringBuilder(); var styles = new List<AdvTextToken>();
                bool content = false;
                foreach (var token in AdvTokens(formatted))
                {
                    if (token.Text == "\n") { output.Append('\n'); line.Length = 0; line.Append(AdvOpenStyles(styles)); content = false; continue; }
                    if (token.Kind == 0 && content && Width(line.ToString() + token.Text + AdvCloseStyles(styles)) > width)
                    { output.Append('\n'); line.Length = 0; line.Append(AdvOpenStyles(styles)); content = false; }
                    output.Append(token.Text); line.Append(token.Text); AdvStyle(styles, token);
                    if (token.Kind == 0) content = true;
                }
                return output.ToString().Trim('\n');
            }
            AdvTextPage Render(string source, List<AdvTextToken> tokens, int start, int end)
            {
                var styles = new List<AdvTextToken>();
                for (int i = 0; i < start; i++) AdvStyle(styles, tokens[i]);
                var b = new StringBuilder(AdvOpenStyles(styles));
                for (int i = start; i < end; i++) { b.Append(tokens[i].Text); AdvStyle(styles, tokens[i]); }
                b.Append(AdvCloseStyles(styles));
                string argument = AdvDisplayFallback(b.ToString()).Trim('\n');
                string display = RepairWideLines(NativeFormat(argument));
                if (Width(display) > width + .01f || Height(display) > height + .01f) return null;
                int begin = start == 0 ? 0 : tokens[start - 1].End;
                return new AdvTextPage { End = end, Original = source.Substring(begin, tokens[end - 1].End - begin), Argument = display, Display = display };
            }
            internal List<AdvTextPage> Pages(string source, List<AdvTextToken> tokens, int start)
            {
                var pages = new List<AdvTextPage>();
                while (start < tokens.Count)
                {
                    int low = start, high = start + 1; AdvTextPage best = null;
                    while (true)
                    {
                        var candidate = Render(source, tokens, start, high);
                        if (candidate == null) break;
                        low = high; best = candidate;
                        if (high == tokens.Count) break;
                        high = Math.Min(tokens.Count, start + (high - start) * 2);
                    }
                    while (low + 1 < high)
                    {
                        int middle = low + (high - low) / 2; var candidate = Render(source, tokens, start, middle);
                        if (candidate == null) high = middle; else { low = middle; best = candidate; }
                    }
                    if (best == null || AdvStyleTag.Replace(best.Original, "").Trim().Length == 0 && low < tokens.Count)
                        throw new BridgeException("dialogue_text_does_not_fit", "A text element or format cannot fit the current native ADV rectangle");
                    if (low < tokens.Count)
                        for (int i = low; i > start + (low - start) * 3 / 4; i--)
                        {
                            string token = tokens[i - 1].Text;
                            if (tokens[i - 1].Kind == 0 && (char.IsWhiteSpace(token[0]) || "。！？!?；;，,、：:.…".IndexOf(token[token.Length - 1]) >= 0))
                            { var candidate = Render(source, tokens, start, i); if (candidate != null) { best = candidate; break; } }
                        }
                    pages.Add(best); start = best.End;
                }
                return pages;
            }
            public void Dispose()
            {
                copy.font = null;
                // Unity 5's explicit Dispose does not suppress its unconditional native finalizer.
                GC.SuppressFinalize(generator); ((IDisposable)generator).Dispose(); UnityEngine.Object.Destroy(copyObject);
            }
        }
        AdvPageLayout NativeAdvPageLayout(ADV.TextScenario scenario)
        {
            var text = scenario == null || scenario.textController == null ? null : AccessTools.Field(typeof(TextController), "messageWindow").GetValue(scenario.textController) as Text;
            if (text == null) throw new BridgeException("dialogue_layout_unavailable", "A ready native ADV text component is required");
            return new AdvPageLayout(text);
        }
    }
}
