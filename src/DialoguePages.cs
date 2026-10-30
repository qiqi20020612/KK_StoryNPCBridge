using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        const int AdvPageCharacters = 50;
        readonly List<string> presentedPages = new List<string>();
        readonly List<string> displayPages = new List<string>();
        int presentedPageIndex;
        int nonBmpFallbacks;
        static IEnumerable<string> AdvPages(string text)
        {
            var elements = StringInfo.GetTextElementEnumerator(text);
            var page = new StringBuilder(); int count = 0;
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement(); page.Append(element); count++;
                if (count >= AdvPageCharacters || element == "\n" || element == "\r\n")
                { yield return page.ToString(); page.Length = 0; count = 0; }
            }
            if (page.Length > 0) yield return page.ToString();
        }
        List<string> BeginAdvPages(string text)
        {
            presentedPages.Clear(); presentedPages.AddRange(AdvPages(text)); presentedPageIndex = 0;
            displayPages.Clear(); nonBmpFallbacks = 0;
            foreach (string page in presentedPages)
            {
                var display = new StringBuilder();
                for (int i = 0; i < page.Length; i++)
                {
                    // Unity 5's native typewriter measures individual UTF-16 units.
                    if (char.IsHighSurrogate(page[i]) && i + 1 < page.Length && char.IsLowSurrogate(page[i + 1]))
                    { display.Append('\u25a1'); i++; nonBmpFallbacks++; }
                    else display.Append(page[i]);
                }
                displayPages.Add(display.ToString());
            }
            return displayPages;
        }
        void TrackAdvPage(string text)
        {
            if (!OwnedTalkActive || presentedPageIndex >= displayPages.Count || displayPages[presentedPageIndex] != text) return;
            string original = presentedPages[presentedPageIndex];
            presentedPageIndex++;
            AddEvent("dialogue_page", Json.Map("conversationId", DialogueActive ? dialogueId : null, "turn", dialogueTurn,
                "page", presentedPageIndex, "pageCount", presentedPages.Count, "text", original, "displayText", text, "maxCharacters", AdvPageCharacters));
        }
        object ReadAdvPagination()
        { return Json.Map("page", presentedPageIndex, "pageCount", presentedPages.Count, "maxCharacters", AdvPageCharacters, "countUnit", "unicode_text_element", "nonBmpFallbacks", nonBmpFallbacks); }
    }
}
