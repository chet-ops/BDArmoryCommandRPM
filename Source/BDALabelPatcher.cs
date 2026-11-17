// BDArmoryCommandRPM - replaces the "CREW" label with "WEAPONS" on the ASET ALCOR MFDs and the basic RPM MFD.
//
// The button labels of the ALCOR MFDs are written inside each page's .txt file. To avoid
// touching ASET's files, copies with the new label are generated in GameData/BDArmoryCommandRPM/Labels/
// when KSP starts, and the ModuleManager patch makes the pages read those copies.

using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BDArmoryCommandRPM
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class BDALabelPatcher : MonoBehaviour
    {
        private static readonly string[] SourceDirs =
        {
            "ASET/ASET_Props/MFDs/ALCORMFD40x20",
            "ASET/ASET_Props/MFDs/ALCORMFD60x30",
            "ASET/ASET_Props/MFDs/ALCORPFD",
            "JSI/RPMPodPatches/BasicMFD",
        };

        private const string NewLabel = "WEAPONS";
        private const string LabelTag = "[font0][#CEE3F6FF]";
        private static readonly Regex TagRegex = new Regex(@"\[[^\]]*\]");
        private static readonly Regex TrailingTags = new Regex(@"(\[[^\]]*\]\s*)+$");

        public void Awake()
        {
            try
            {
                string root = KSPUtil.ApplicationRootPath + "GameData/";
                int written = 0;
                foreach (string dir in SourceDirs)
                {
                    string src = Path.Combine(root, dir);
                    if (!Directory.Exists(src)) continue;
                    string dst = Path.Combine(root, "BDArmoryCommandRPM/Labels/" + dir);
                    Directory.CreateDirectory(dst);

                    foreach (string file in Directory.GetFiles(src, "*.txt"))
                    {
                        string text = File.ReadAllText(file, Encoding.UTF8);
                        string result = Relabel(text);
                        string dstFile = Path.Combine(dst, Path.GetFileName(file));
                        if (File.Exists(dstFile) && File.ReadAllText(dstFile, Encoding.UTF8) == result) continue;
                        File.WriteAllText(dstFile, result, new UTF8Encoding(false));
                        written++;
                    }
                }
                Debug.Log("[BDArmoryCommandRPM] WEAPONS labels generated (" + written + " files updated)");
            }
            catch (Exception e)
            {
                Debug.LogError("[BDArmoryCommandRPM] Could not generate the labels: " + e);
            }
        }

        private static string Relabel(string text)
        {
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i];
                if (l.IndexOf("[hw]", StringComparison.Ordinal) < 0 || l.IndexOf('|') < 0) continue;
                int crew = l.IndexOf("CREW", StringComparison.Ordinal);
                if (crew < 0) continue;

                int prevPipe = l.LastIndexOf('|', crew);
                int start = prevPipe >= 0 ? prevPipe + 1 : l.IndexOf("[hw]", StringComparison.Ordinal) + 4;
                int end = l.IndexOf('|', crew);                     // next separator
                if (end < 0) end = l.TrimEnd('\r').Length;

                string segment = l.Substring(start, end - start);
                int visible = TagRegex.Replace(segment, string.Empty).Length;
                Match trailing = TrailingTags.Match(segment);
                string keepTags = trailing.Success ? trailing.Value.Trim() : string.Empty;

                string label = NewLabel.Length >= visible ? NewLabel.Substring(0, visible)
                    : new string(' ', (visible - NewLabel.Length) / 2) + NewLabel;
                label = label.PadRight(visible);

                lines[i] = l.Substring(0, start) + LabelTag + label + "[font0]" + keepTags + l.Substring(end);
            }
            return string.Join("\n", lines);
        }
    }
}
