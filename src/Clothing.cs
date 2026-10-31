using System;
using System.Collections.Generic;

namespace KK_StoryNPCBridge
{
    public sealed partial class Plugin
    {
        static Dictionary<string, object> ClothingTypeReference(int id)
        {
            string native = Enum.GetName(typeof(ChaFileDefine.CoordinateType), id), name;
            switch ((ChaFileDefine.CoordinateType)id)
            {
                case ChaFileDefine.CoordinateType.School01: name = "School uniform 1"; break;
                case ChaFileDefine.CoordinateType.School02: name = "School uniform 2"; break;
                case ChaFileDefine.CoordinateType.Gym: name = "Gym outfit"; break;
                case ChaFileDefine.CoordinateType.Swim: name = "Swimsuit"; break;
                case ChaFileDefine.CoordinateType.Club: name = "Club outfit"; break;
                case ChaFileDefine.CoordinateType.Plain: name = "Casual outfit"; break;
                case ChaFileDefine.CoordinateType.Pajamas: name = "Pajamas"; break;
                default: name = "Unknown clothing type " + id; break;
            }
            return Json.Map("id", id, "key", "coordinate/" + id, "name", name, "nativeName", native, "known", native != null, "readOnly", true,
                "description", native == null ? "Unrecognized native coordinate slot; the actual ID is preserved without guessing its meaning."
                    : "Native " + native + " coordinate slot. Clothes assigned to this slot can differ by character card; this is not garment recognition or undressing state.");
        }
        static object ReadClothingCatalog()
        {
            var rows = new List<object>();
            foreach (ChaFileDefine.CoordinateType type in Enum.GetValues(typeof(ChaFileDefine.CoordinateType))) rows.Add(ClothingTypeReference((int)type));
            return rows;
        }
        object ReadClothing()
        {
            string reason = ownedHeroine == null ? "story_not_ready" : SceneBusy ? "scene_transition"
                : !VisualActorReady || ownedCha.fileStatus == null ? "model_not_loaded" : null;
            return Json.Map("available", reason == null, "unavailableReason", reason, "readOnly", true,
                "source", reason == null ? "native_model" : null, "nativeField", "ChaControl.fileStatus.coordinateType",
                "context", reason != null ? null : OwnedTalkActive ? "native_talk" : Mode == "roaming" ? "roaming" : "native_scene",
                "type", reason == null ? ClothingTypeReference(ownedCha.fileStatus.coordinateType) : null);
        }
    }
}
