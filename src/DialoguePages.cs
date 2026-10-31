using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        const int AdvPageWidthUnits = 300;
        const int AdvAsciiWidthUnits = 3;
        const int AdvNonAsciiWidthUnits = 5;
        readonly List<string> presentedPages = new List<string>();
        readonly List<string> displayPages = new List<string>();
        int presentedPageIndex;
        int nonBmpFallbacks;
        static IEnumerable<string> AdvPages(string text)
        {
            var elements = StringInfo.GetTextElementEnumerator(text);
            var page = new StringBuilder(); int width = 0;
            while (elements.MoveNext())
            {
                string element = elements.GetTextElement();
                int units = element[0] <= '\u007f' ? AdvAsciiWidthUnits : AdvNonAsciiWidthUnits;
                if (width + units > AdvPageWidthUnits)
                { yield return page.ToString(); page.Length = 0; width = 0; }
                page.Append(element); width += units;
                if (width >= AdvPageWidthUnits || element == "\n" || element == "\r\n")
                { yield return page.ToString(); page.Length = 0; width = 0; }
            }
            if (page.Length > 0) yield return page.ToString();
        }
        List<string> BeginAdvPages(string text)
        { return BeginAdvPages(AdvPages(text)); }
        List<string> BeginAdvPages(IEnumerable<string> pages)
        {
            presentedPages.Clear(); presentedPages.AddRange(pages); presentedPageIndex = 0;
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
            var data = Json.Map("conversationId", DialogueActive ? dialogueId : null, "turn", dialogueTurn,
                "page", presentedPageIndex, "pageCount", presentedPages.Count, "text", original, "displayText", text,
                "maxCharacters", AdvPageWidthUnits / AdvAsciiWidthUnits, "maxCjkCharacters", AdvPageWidthUnits / AdvNonAsciiWidthUnits,
                "maxWidthUnits", AdvPageWidthUnits, "countUnit", "weighted_text_element", "asciiWidthUnits", AdvAsciiWidthUnits, "nonAsciiWidthUnits", AdvNonAsciiWidthUnits);
            TrackAdvSequencePage(data); AddEvent("dialogue_page", data);
        }
        object ReadAdvPagination()
        { return Json.Map("page", presentedPageIndex, "pageCount", presentedPages.Count,
            "maxCharacters", AdvPageWidthUnits / AdvAsciiWidthUnits, "maxCjkCharacters", AdvPageWidthUnits / AdvNonAsciiWidthUnits,
            "maxWidthUnits", AdvPageWidthUnits, "countUnit", "weighted_text_element", "asciiWidthUnits", AdvAsciiWidthUnits, "nonAsciiWidthUnits", AdvNonAsciiWidthUnits, "nonBmpFallbacks", nonBmpFallbacks); }
    }
}
