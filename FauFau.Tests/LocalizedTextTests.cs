using System.Linq;
using System.Text;
using FauFau.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace FauFau.Tests
{
    [TestClass]
    public class LocalizedTextTests
    {
        private static byte[] Value(byte[] header, string text)
        {
            return header.Concat(Encoding.UTF8.GetBytes(text)).Append((byte)0).ToArray();
        }

        [TestMethod]
        public void Decode_WithoutHeader_ReturnsText()
        {
            string text = LocalizedText.Decode(Value(new byte[0], "Thumper"));

            text.ShouldBe("Thumper");
        }

        [TestMethod]
        public void Decode_OneParameter_InsertsPlaceholder()
        {
            string text = LocalizedText.Decode(Value(new byte[] { 1, 0, 0, 6 }, "Round "));

            text.ShouldBe("Round {0}");
        }

        [TestMethod]
        public void Decode_ParametersOutOfOrder_InsertsEach()
        {
            string text = LocalizedText.Decode(Value(new byte[] { 2, 1, 0, 11, 0, 0, 6 }, "Runde  von "));

            text.ShouldBe("Runde {0} von {1}");
        }

        [TestMethod]
        public void Decode_OffsetsCountUtf8Bytes()
        {
            string text = LocalizedText.Decode(Value(new byte[] { 2, 1, 0, 17, 0, 0, 11 }, "Раунд  из "));

            text.ShouldBe("Раунд {0} из {1}");
        }

        [TestMethod]
        public void Decode_OffsetInsideCharacter_CountsCharacters()
        {
            string text = LocalizedText.Decode(Value(new byte[] { 1, 0, 0, 8 }, "대기열 순서: /{1]"));

            text.ShouldBe("대기열 순서: {0}/{1]");
        }

        [TestMethod]
        public void Decode_HeaderOnly_IsPlaceholder()
        {
            string text = LocalizedText.Decode(Value(new byte[] { 1, 0, 0, 0 }, ""));

            text.ShouldBe("{0}");
        }

        [TestMethod]
        public void Decode_OffsetPastText_DropsHeader()
        {
            string text = LocalizedText.Decode(Value(new byte[] { 1, 0, 0, 40 }, "Round "));

            text.ShouldBe("Round ");
        }

        [TestMethod]
        public void Decode_String_MatchesBytes()
        {
            string text = LocalizedText.Decode("\u0002\u0001\u0000\u0011\u0000\u0000\u000BРаунд  из ");

            text.ShouldBe("Раунд {0} из {1}");
        }
    }
}
