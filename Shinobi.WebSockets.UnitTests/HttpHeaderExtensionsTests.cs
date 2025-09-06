using System;
using System.Linq;
using Xunit;
using Shinobi.WebSockets.Http;
using Shinobi.WebSockets.Internal;

namespace Shinobi.WebSockets.UnitTests
{
    public class HttpHeaderExtensionsTests
    {
        [Fact]
        public void ParseCommaSeparated_WithNull_ShouldReturnEmptyArray()
        {
            // Act
            var result = ((string?)null).ParseCommaSeparated();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseCommaSeparated_WithEmptyString_ShouldReturnEmptyArray()
        {
            // Act
            var result = "".ParseCommaSeparated();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseCommaSeparated_WithSingleValue_ShouldReturnSingleValue()
        {
            // Act
            var result = "permessage-deflate".ParseCommaSeparated();

            // Assert
            Assert.Single(result);
            Assert.Equal("permessage-deflate", result[0]);
        }

        [Fact]
        public void ParseCommaSeparated_WithMultipleValues_ShouldReturnAllValues()
        {
            // Act
            var result = "permessage-deflate, x-webkit-deflate-frame, compression".ParseCommaSeparated();

            // Assert
            Assert.Equal(3, result.Length);
            Assert.Equal("permessage-deflate", result[0]);
            Assert.Equal("x-webkit-deflate-frame", result[1]);
            Assert.Equal("compression", result[2]);
        }

        [Fact]
        public void ParseCommaSeparated_WithQuotedValues_ShouldHandleQuotes()
        {
            // Act
            var result = "\"quoted value\", normal, \"another quoted\"".ParseCommaSeparated();

            // Assert
            Assert.Equal(3, result.Length);
            Assert.Equal("\"quoted value\"", result[0]);
            Assert.Equal("normal", result[1]);
            Assert.Equal("\"another quoted\"", result[2]);
        }

        [Fact]
        public void ParseCommaSeparated_WithQuotedCommas_ShouldNotSplitOnQuotedCommas()
        {
            // Act
            var result = "\"value, with, commas\", second".ParseCommaSeparated();

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("\"value, with, commas\"", result[0]);
            Assert.Equal("second", result[1]);
        }

        [Fact]
        public void ParseCommaSeparated_WithEscapedQuotes_ShouldHandleEscapedQuotes()
        {
            // Act
            var result = "\"escaped\\\"quote\", normal".ParseCommaSeparated();

            // Assert
            Assert.Equal(2, result.Length);
            Assert.Equal("\"escaped\\\"quote\"", result[0]);
            Assert.Equal("normal", result[1]);
        }

        [Fact]
        public void ParseCommaSeparated_WithExtraSpaces_ShouldTrimValues()
        {
            // Act
            var result = "  value1  ,  value2  ,   value3   ".ParseCommaSeparated();

            // Assert
            Assert.Equal(3, result.Length);
            Assert.Equal("value1", result[0]);
            Assert.Equal("value2", result[1]);
            Assert.Equal("value3", result[2]);
        }

        [Fact]
        public void ParseCommaSeparated_WithEmptyValues_ShouldSkipEmptyValues()
        {
            // Act
            var result = "value1,,value2,   ,value3".ParseCommaSeparated();

            // Assert
            Assert.Equal(3, result.Length);
            Assert.Equal("value1", result[0]);
            Assert.Equal("value2", result[1]);
            Assert.Equal("value3", result[2]);
        }

        [Fact]
        public void ParseExtension_WithNull_ShouldReturnNull()
        {
            // Act
            var result = ((string?)null).ParseExtension();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParseExtension_WithEmptyString_ShouldReturnNull()
        {
            // Act
            var result = "".ParseExtension();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParseExtension_WithSimpleName_ShouldReturnExtensionWithName()
        {
            // Act
            var result = "permessage-deflate".ParseExtension();

            // Assert
            Assert.NotNull(result);
            Assert.Equal("permessage-deflate", result.Name);
            Assert.Empty(result.Parameters);
        }

        [Fact]
        public void ParseExtension_WithNameAndParameters_ShouldParseParameters()
        {
            // Act
            var result = "permessage-deflate; server_no_context_takeover; client_max_window_bits=15".ParseExtension();

            // Assert
            Assert.NotNull(result);
            Assert.Equal("permessage-deflate", result.Name);
            Assert.Equal(2, result.Parameters.Count);
            Assert.True((bool)result.Parameters["server_no_context_takeover"]);
            Assert.Equal("15", result.Parameters["client_max_window_bits"]);
        }

        [Fact]
        public void ParseExtension_WithQuotedParameterValues_ShouldRemoveQuotes()
        {
            // Act
            var result = "custom-extension; param1=\"quoted value\"; param2=unquoted".ParseExtension();

            // Assert
            Assert.NotNull(result);
            Assert.Equal("custom-extension", result.Name);
            Assert.Equal(2, result.Parameters.Count);
            Assert.Equal("quoted value", result.Parameters["param1"]);
            Assert.Equal("unquoted", result.Parameters["param2"]);
        }

        [Fact]
        public void ParseExtension_WithBooleanParameters_ShouldSetToTrue()
        {
            // Act
            var result = "compression; enable_compression; use_gzip; max_size=1024".ParseExtension();

            // Assert
            Assert.NotNull(result);
            Assert.Equal("compression", result.Name);
            Assert.Equal(3, result.Parameters.Count);
            Assert.True((bool)result.Parameters["enable_compression"]);
            Assert.True((bool)result.Parameters["use_gzip"]);
            Assert.Equal("1024", result.Parameters["max_size"]);
        }

        [Fact]
        public void ParseExtension_WithExtraSpaces_ShouldTrimAllValues()
        {
            // Act
            var result = "  extension-name  ;  param1 =  value1  ;  flag_param  ".ParseExtension();

            // Assert
            Assert.NotNull(result);
            Assert.Equal("extension-name", result.Name);
            Assert.Equal(2, result.Parameters.Count);
            Assert.Equal("value1", result.Parameters["param1"]);
            Assert.True((bool)result.Parameters["flag_param"]);
        }

        [Fact]
        public void ParseExtension_WithEmptyParameterValue_ShouldStoreEmptyString()
        {
            // Act
            var result = "extension; param1=; param2=value".ParseExtension();

            // Assert
            Assert.NotNull(result);
            Assert.Equal("extension", result.Name);
            Assert.Equal(2, result.Parameters.Count);
            Assert.Equal("", result.Parameters["param1"]);
            Assert.Equal("value", result.Parameters["param2"]);
        }

        [Fact]
        public void ParseExtension_WithQuotedEmptyValue_ShouldStoreEmptyString()
        {
            // Act
            var result = "extension; param1=\"\"; param2=\"value\"".ParseExtension();

            // Assert
            Assert.NotNull(result);
            Assert.Equal("extension", result.Name);
            Assert.Equal(2, result.Parameters.Count);
            Assert.Equal("", result.Parameters["param1"]);
            Assert.Equal("value", result.Parameters["param2"]);
        }

        [Fact]
        public void ParseExtensions_WithNull_ShouldReturnNull()
        {
            // Act
            var result = ((string?)null).ParseExtensions();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParseExtensions_WithEmptyString_ShouldReturnNull()
        {
            // Act
            var result = "".ParseExtensions();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void ParseExtensions_WithSingleExtension_ShouldReturnSingleExtension()
        {
            // Act
            var result = "permessage-deflate; server_no_context_takeover".ParseExtensions();

            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal("permessage-deflate", result[0].Name);
            Assert.True((bool)result[0].Parameters["server_no_context_takeover"]);
        }

        [Fact]
        public void ParseExtensions_WithMultipleExtensions_ShouldReturnAllExtensions()
        {
            // Act
            var result = "permessage-deflate; server_no_context_takeover, x-webkit-deflate-frame, compression; level=9".ParseExtensions();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Length);

            Assert.Equal("permessage-deflate", result[0].Name);
            Assert.True((bool)result[0].Parameters["server_no_context_takeover"]);

            Assert.Equal("x-webkit-deflate-frame", result[1].Name);
            Assert.Empty(result[1].Parameters);

            Assert.Equal("compression", result[2].Name);
            Assert.Equal("9", result[2].Parameters["level"]);
        }

        [Fact]
        public void ParseExtensions_WithComplexExtensions_ShouldParseAll()
        {
            // Act
            var extensionHeader = "permessage-deflate; client_max_window_bits=15; server_max_window_bits=\"10\"; server_no_context_takeover, " +
                                "custom-extension; param1=\"quoted value\"; flag_param, " +
                                "another-extension";
            var result = extensionHeader.ParseExtensions();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(3, result.Length);

            // First extension
            Assert.Equal("permessage-deflate", result[0].Name);
            Assert.Equal(3, result[0].Parameters.Count);
            Assert.Equal("15", result[0].Parameters["client_max_window_bits"]);
            Assert.Equal("10", result[0].Parameters["server_max_window_bits"]);
            Assert.True((bool)result[0].Parameters["server_no_context_takeover"]);

            // Second extension
            Assert.Equal("custom-extension", result[1].Name);
            Assert.Equal(2, result[1].Parameters.Count);
            Assert.Equal("quoted value", result[1].Parameters["param1"]);
            Assert.True((bool)result[1].Parameters["flag_param"]);

            // Third extension
            Assert.Equal("another-extension", result[2].Name);
            Assert.Empty(result[2].Parameters);
        }

        [Fact]
        public void ParseExtensions_WithInvalidExtensions_ShouldSkipInvalid()
        {
            // Act - including empty extension segments
            var result = "valid-extension, , another-valid; param=value".ParseExtensions();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Length);
            Assert.Equal("valid-extension", result[0].Name);
            Assert.Equal("another-valid", result[1].Name);
            Assert.Equal("value", result[1].Parameters["param"]);
        }

        [Theory]
        [InlineData("dGhlIHNhbXBsZSBub25jZQ==", "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=")]
        public void ComputeSocketAcceptString_WithValidKeys_ShouldReturnCorrectAcceptString(string key, string expectedAccept)
        {
            // Act
            var result = key.ComputeSocketAcceptString();

            // Assert
            Assert.Equal(expectedAccept, result);
        }

        [Fact]
        public void ComputeSocketAcceptString_WithEmptyKey_ShouldReturnValidBase64()
        {
            // Act
            var result = "".ComputeSocketAcceptString();

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result);
            // Should be valid base64
            var bytes = Convert.FromBase64String(result);
            Assert.Equal(20, bytes.Length); // SHA1 produces 20-byte hash
        }

        [Fact]
        public void ComputeSocketAcceptString_IsConsistent_ShouldReturnSameResultForSameInput()
        {
            // Arrange
            var key = "testKey123";

            // Act
            var result1 = key.ComputeSocketAcceptString();
            var result2 = key.ComputeSocketAcceptString();

            // Assert
            Assert.Equal(result1, result2);
        }
    }
}