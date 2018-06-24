using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BizTalkComponents.Utils;
using Microsoft.BizTalk.Component.Interop;
using Microsoft.BizTalk.Message.Interop;
using IComponent = Microsoft.BizTalk.Component.Interop.IComponent;
using System.ComponentModel;
using System.Diagnostics;
using System.ComponentModel.DataAnnotations;
using Microsoft.XLANGs.BaseTypes;
using System.IO;
using System.Xml;
using System.Xml.Xsl;
using Microsoft.BizTalk.Streaming;
using System.Text.RegularExpressions;

namespace BizTalkComponents.PipelineComponents.EDIFactSchemaCustomizer
{
    [ComponentCategory(CategoryTypes.CATID_PipelineComponent)]
    [ComponentCategory(CategoryTypes.CATID_Any)]
    [System.Runtime.InteropServices.Guid("9d0e4103-4cce-4536-83fa-4a5040674ad6")]
    public partial class EFactSchemaCustomizer : IBaseComponent, IComponent, IComponentUI, IPersistPropertyBag
    {
        [DisplayName("CharacterSet")]
        [Description("Incoming Message CharacterSet")]
        public string CharSet { get; set; }


        [DisplayName("EDIFact Delimiters"), RequiredRuntime]
        [RegularExpression(@"^(\s*(0x[0-9a-fA-F]{2})\s*(,|$)){6,8}")]
        [Description("Hexadecimal delimiters separated by space and comma."
            + "They should be specified in the same order as defined in UNA segment, component separator, data element separator, decimal notation, release indicator, "
            + "repetition separator (anything other than space character), segment terminator, segment terminator suffix.")]
        public string EfactDelimiters { get; set; }

        [Description("e.g. EFACT_D96A_ORDERS_CUSTOM")]
        [DisplayName("Root Node Extension"), RequiredRuntime]
        public string RootNodeExtension { get; set; }
        
        private string del_SuffixSigTerm;
        private Dictionary<string, char> delimiters = new Dictionary<string, char>();

        public IBaseMessage Execute(IPipelineContext pContext, IBaseMessage pInMsg)
        {
            if (!Enabled)
            {
                return pInMsg;
            }
            string errorMessage;
            if (!Validate(out errorMessage))
            {
                throw new ArgumentException(errorMessage);
            }
            Encoding encoder = string.IsNullOrEmpty(CharSet) ? Encoding.Default : Encoding.GetEncoding(CharSet);
            var match = Regex.Match(EfactDelimiters, @"^(?:\s*(0x[0-9a-fA-F]{2})\s*(?:,|$)){6,8}");
            delimiters["Component"] = (char)Convert.ToInt32(match.Groups[1].Captures[0].Value, 16);
            delimiters["DataElement"] = (char)Convert.ToInt32(match.Groups[1].Captures[1].Value, 16);
            delimiters["DecimalNotation"] = (char)Convert.ToInt32(match.Groups[1].Captures[2].Value, 16);
            delimiters["ReleaseIndicator"] = (char)Convert.ToInt32(match.Groups[1].Captures[3].Value, 16);
            delimiters["RepetitionSeparator"] = (char)Convert.ToInt32(match.Groups[1].Captures[4].Value, 16);
            delimiters["SigmentTerminator"] = (char)Convert.ToInt32(match.Groups[1].Captures[5].Value, 16);
            del_SuffixSigTerm = "";
            if (match.Groups[1].Captures.Count > 6)
            {
                del_SuffixSigTerm += (char)Convert.ToInt32(match.Groups[1].Captures[6].Value, 16);
                if (match.Groups[1].Captures.Count > 7)
                    del_SuffixSigTerm += (char)Convert.ToInt32(match.Groups[1].Captures[7].Value, 16);
            }

            Stream origStream = pInMsg.BodyPart.GetOriginalDataStream();
            StreamReader reader = new StreamReader(origStream, encoder);

            char ch;
            bool UNASeg = false;
            StringBuilder sb = new StringBuilder();
            char[] buff = new char[3];
            //Read the first segment, if it is UNA segment then update delimiters.
            reader.ReadBlock(buff, 0, buff.Length);
            sb.Append(buff);
            UNASeg = sb.ToString().StartsWith("UNA");
            if (UNASeg)
            {
                buff = new char[6];
                reader.Read(buff, 0, buff.Length);
                sb.Append(buff);
                delimiters["Component"] = buff[0];
                delimiters["DataElement"] = buff[1];
                delimiters["DecimalNotation"] = buff[2];
                delimiters["ReleaseIndicator"] = buff[3];
                delimiters["RepetitionSeparator"] = buff[4];
                delimiters["SigmentTerminator"] = buff[5];
                ch = (char)reader.Peek();
                //Search for suffix segment terminator if exists.
                List<char> suffix_SegmentTerminator = new List<char>();
                if (ch == '\r')
                {
                    ch = (char)reader.Read();
                    suffix_SegmentTerminator.Add(ch);
                    ch = (char)reader.Peek();
                    if (ch == '\n')
                    {
                        ch = (char)reader.Read();
                        suffix_SegmentTerminator.Add(ch);
                    }
                }
                else if (ch == '\n')
                {
                    ch = (char)reader.Read();
                    suffix_SegmentTerminator.Add(ch);
                }
                del_SuffixSigTerm = new string(suffix_SegmentTerminator.ToArray());
            }
            string segment = "";
            MemoryStream ms = new MemoryStream();
            StreamWriter writer = new StreamWriter(ms, encoder);
            writer.NewLine = del_SuffixSigTerm;
            bool readLines = !string.IsNullOrEmpty(del_SuffixSigTerm);
            if (UNASeg)
            {
                writer.WriteSegment(sb.ToString(), readLines);
                sb.Clear();
            }
            bool eos = false; //end of segement;
            while (reader.Peek() >= 0)
            {
                if (readLines)
                    segment = reader.ReadLine();
                else
                {
                    ch = (char)reader.Read();
                    sb.Append(ch);
                    eos = ch == delimiters["SigmentTerminator"];
                }
                if (eos | readLines)
                {
                    if (eos)
                    {
                        segment = sb.ToString();
                        sb.Clear();
                        eos = false;
                    }
                    if (segment.StartsWith("UNH"))
                    {
                        segment = segment.Remove(segment.Length - 1);
                        string[] components = segment.Split(delimiters["DataElement"]);
                        List<string> elements = new List<string>(components[2].Split(delimiters["Component"]));
                        while (elements.Count < 5)
                            elements.Add(string.Empty);
                        elements[4] = RootNodeExtension;
                        components[2] = string.Join(delimiters["Component"].ToString(), elements);
                        segment = string.Join(delimiters["DataElement"].ToString(), components);
                        segment += delimiters["SigmentTerminator"];
                    }
                    writer.WriteSegment(segment, readLines);
                    segment = "";
                }
            }
            writer.Flush();
            ms.Seek(0, SeekOrigin.Begin);
            pInMsg.BodyPart.Data = ms;
            pInMsg.BodyPart.Charset = CharSet;
            pContext.ResourceTracker.AddResource(pInMsg.BodyPart.Data);
            return pInMsg;
        }


    }
    public static class extensions
    {
        public static void WriteSegment(this StreamWriter writer, string segment, bool writeLine)
        {
            if (writeLine)
                writer.WriteLine(segment);
            else
                writer.Write(segment);
        }
    }

}
