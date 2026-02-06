using System;
using System.Collections.Generic;
using System.Text;
using NatsWebSocket.JetStream.Internal;
using Xunit;

namespace NatsWebSocket.Tests.JetStream
{
    public class Base64UrlTests
    {
        #region Encode Tests

        [Fact]
        public void Encode_SimpleString_ReturnsBase64Url()
        {
            var result = Base64Url.Encode("hello");
            Assert.Equal("aGVsbG8", result);
        }

        [Fact]
        public void Encode_EmptyString_ReturnsEmpty()
        {
            Assert.Equal("", Base64Url.Encode(""));
            Assert.Equal("", Base64Url.Encode((string)null));
        }

        [Fact]
        public void Encode_EmptyBytes_ReturnsEmpty()
        {
            Assert.Equal("", Base64Url.Encode(Array.Empty<byte>()));
            Assert.Equal("", Base64Url.Encode((byte[])null));
        }

        [Fact]
        public void Encode_NoPadding_OmitsPaddingChars()
        {
            // "a" in base64 is "YQ==" but base64url should be "YQ"
            var result = Base64Url.Encode("a");
            Assert.DoesNotContain("=", result);
        }

        [Fact]
        public void Encode_UrlSafeChars_ReplacesSpecialChars()
        {
            // Create input that produces + and / in regular base64
            // The bytes [251, 239] produce "++" in standard base64
            var bytes = new byte[] { 251, 239 };
            var result = Base64Url.Encode(bytes);

            Assert.DoesNotContain("+", result);
            Assert.DoesNotContain("/", result);
            Assert.Contains("-", result); // + becomes -
        }

        [Fact]
        public void Encode_SpecialCharacters_HandlesUnicode()
        {
            var result = Base64Url.Encode("日本語");
            Assert.NotEmpty(result);
            // Verify round-trip
            var decoded = Base64Url.Decode(result);
            Assert.Equal("日本語", decoded);
        }

        [Fact]
        public void Encode_ObjectName_HandlesSlashesAndDots()
        {
            var result = Base64Url.Encode("path/to/file.txt");
            Assert.NotEmpty(result);
            Assert.DoesNotContain("/", result);
            var decoded = Base64Url.Decode(result);
            Assert.Equal("path/to/file.txt", decoded);
        }

        #endregion

        #region Decode Tests

        [Fact]
        public void Decode_ValidBase64Url_ReturnsString()
        {
            var result = Base64Url.Decode("aGVsbG8");
            Assert.Equal("hello", result);
        }

        [Fact]
        public void Decode_EmptyString_ReturnsEmpty()
        {
            Assert.Equal("", Base64Url.Decode(""));
            Assert.Equal("", Base64Url.Decode(null));
        }

        [Fact]
        public void Decode_WithoutPadding_HandlesMissingPadding()
        {
            // "YQ" should decode to "a" (normally needs "YQ==")
            var result = Base64Url.Decode("YQ");
            Assert.Equal("a", result);
        }

        [Fact]
        public void Decode_WithUrlSafeChars_ConvertsBack()
        {
            // Test with a known value that uses - and _
            var original = new byte[] { 251, 239, 190 };
            var encoded = Base64Url.Encode(original);
            var decoded = Base64Url.DecodeBytes(encoded);

            Assert.Equal(original, decoded);
        }

        #endregion

        #region RoundTrip Tests

        [Theory]
        [InlineData("")]
        [InlineData("a")]
        [InlineData("ab")]
        [InlineData("abc")]
        [InlineData("hello world")]
        [InlineData("user-123/document.pdf")]
        [InlineData("file with spaces.txt")]
        [InlineData("special!@#$%^&*()chars")]
        [InlineData("emoji🎉test")]
        public void RoundTrip_String_PreservesValue(string input)
        {
            var encoded = Base64Url.Encode(input);
            var decoded = Base64Url.Decode(encoded);
            Assert.Equal(input, decoded);
        }

        [Fact]
        public void RoundTrip_RandomBytes_PreservesValue()
        {
            var random = new Random(42);
            for (int length = 0; length < 100; length++)
            {
                var bytes = new byte[length];
                random.NextBytes(bytes);

                var encoded = Base64Url.Encode(bytes);
                var decoded = Base64Url.DecodeBytes(encoded);

                Assert.Equal(bytes, decoded);
            }
        }

        #endregion
    }

    public class NuidTests
    {
        [Fact]
        public void Next_ReturnsCorrectLength()
        {
            var nuid = Nuid.Next();
            Assert.Equal(22, nuid.Length);
        }

        [Fact]
        public void Next_ContainsOnlyValidChars()
        {
            var validChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

            for (int i = 0; i < 100; i++)
            {
                var nuid = Nuid.Next();
                foreach (var c in nuid)
                {
                    Assert.Contains(c, validChars);
                }
            }
        }

        [Fact]
        public void Next_GeneratesUniqueValues()
        {
            var nuids = new HashSet<string>();

            for (int i = 0; i < 10000; i++)
            {
                var nuid = Nuid.Next();
                Assert.DoesNotContain(nuid, nuids);
                nuids.Add(nuid);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task Next_IsThreadSafe()
        {
            var nuids = new System.Collections.Concurrent.ConcurrentBag<string>();
            var tasks = new System.Threading.Tasks.Task[10];

            for (int i = 0; i < 10; i++)
            {
                tasks[i] = System.Threading.Tasks.Task.Run(() =>
                {
                    for (int j = 0; j < 1000; j++)
                    {
                        nuids.Add(Nuid.Next());
                    }
                });
            }

            await System.Threading.Tasks.Task.WhenAll(tasks);

            // All 10,000 should be unique
            Assert.Equal(10000, nuids.Count);
            Assert.Equal(10000, new HashSet<string>(nuids).Count);
        }
    }
}
