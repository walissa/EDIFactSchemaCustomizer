using System;
using BizTalkComponents.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Winterdom.BizTalk.PipelineTesting;
using Microsoft.BizTalk.Component;
using System.Net.Mail;
using BizTalkComponents.PipelineComponents.EDIFactSchemaCustomizer;
using System.Text;
using System.IO;
namespace BizTalkComponents.PipelineComponents.EDIFactSchemaCustomizer.Tests.UnitTests
{
    [TestClass]
    public class EFactNamespaceCustomizerTester
    {
        [TestMethod]
        public void AddUNH25()
        {
            var pipeline = PipelineFactory.CreateEmptyReceivePipeline();
            var component = new EFactSchemaCustomizer
            {
                CharSet="utf-8",
                Enabled=true,
                EfactDelimiters = "0x3A, 0x2B, 0x2C, 0x3F, 0x20, 0x27",
                RootNodeExtension="Test",
            };
            pipeline.AddComponent(component,PipelineStage.Decode);

            var stream = new FileStream(@"D:\TFS\Ovako\Repos\INT0063.SalesOrder\Tests\Test Files\201709080520528498.edi", FileMode.Open);
            var message = MessageHelper.CreateFromStream(stream);
            var output = pipeline.Execute(message);            
            var test = MessageHelper.ReadString(message);
        }

    }
}
