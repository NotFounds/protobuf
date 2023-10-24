﻿#region Copyright notice and license
// Protocol Buffers - Google's data interchange format
// Copyright 2015 Google Inc.  All rights reserved.
//
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file or at
// https://developers.google.com/open-source/licenses/bsd
#endregion

using Conformance;
using Google.Protobuf.Reflection;
using System;
using System.IO;

namespace Google.Protobuf.Conformance
{
    /// <summary>
    /// Conformance tests. The test runner will provide JSON or proto data on stdin,
    /// and this program will produce its output on stdout.
    /// </summary>
    class Program
    {
        private static void Main()
        {
            // This way we get the binary streams instead of readers/writers.
            var input = new BinaryReader(Console.OpenStandardInput());
            var output = new BinaryWriter(Console.OpenStandardOutput());
            var typeRegistry = TypeRegistry.FromMessages(
                ProtobufTestMessages.Proto3.TestAllTypesProto3.Descriptor,
                ProtobufTestMessages.Proto2.TestAllTypesProto2.Descriptor);

            int count = 0;
            while (RunTest(input, output, typeRegistry))
            {
                count++;
            }
            Console.Error.WriteLine("Received EOF after {0} tests", count);
        }

        private static bool RunTest(BinaryReader input, BinaryWriter output, TypeRegistry typeRegistry)
        {
            int? size = ReadInt32(input);
            if (size == null)
            {
                return false;
            }
            byte[] inputData = input.ReadBytes(size.Value);
            if (inputData.Length != size.Value)
            {
                throw new EndOfStreamException("Read " + inputData.Length + " bytes of data when expecting " + size);
            }
            ConformanceRequest request = ConformanceRequest.Parser.ParseFrom(inputData);
            ConformanceResponse response = PerformRequest(request, typeRegistry);
            byte[] outputData = response.ToByteArray();
            output.Write(outputData.Length);
            output.Write(outputData);
            // Ready for another test...
            return true;
        }

        private static ConformanceResponse PerformRequest(ConformanceRequest request, TypeRegistry typeRegistry)
        {
            ExtensionRegistry proto2ExtensionRegistry = new ExtensionRegistry
            {
                ProtobufTestMessages.Proto2.TestMessagesProto2Extensions.ExtensionInt32,
                ProtobufTestMessages.Proto2.TestAllTypesProto2.Types.MessageSetCorrectExtension1.Extensions.MessageSetExtension,
                ProtobufTestMessages.Proto2.TestAllTypesProto2.Types.MessageSetCorrectExtension2.Extensions.MessageSetExtension
            };
            IMessage message;
            try
            {
                switch (request.PayloadCase)
                {
                    case ConformanceRequest.PayloadOneofCase.JsonPayload:
                        if (request.TestCategory == global::Conformance.TestCategory.JsonIgnoreUnknownParsingTest)
                        {
                            return new ConformanceResponse { Skipped = "CSharp doesn't support skipping unknown fields in json parsing." };
                        }
                        var parser = new JsonParser(new JsonParser.Settings(20, typeRegistry));
                        message = request.MessageType switch
                        {
                            "protobuf_test_messages.proto3.TestAllTypesProto3" => parser.Parse<ProtobufTestMessages.Proto3.TestAllTypesProto3>(request.JsonPayload),
                            "protobuf_test_messages.proto2.TestAllTypesProto2" => parser.Parse<ProtobufTestMessages.Proto2.TestAllTypesProto2>(request.JsonPayload),
                            _ => throw new Exception($" Protobuf request doesn't have specific payload type ({request.MessageType})"),
                        };
                        break;
                    case ConformanceRequest.PayloadOneofCase.ProtobufPayload:
                        message = request.MessageType switch
                        {
                            "protobuf_test_messages.proto3.TestAllTypesProto3" => ProtobufTestMessages.Proto3.TestAllTypesProto3.Parser.ParseFrom(request.ProtobufPayload),
                            "protobuf_test_messages.proto2.TestAllTypesProto2" => ProtobufTestMessages.Proto2.TestAllTypesProto2.Parser
                                                                .WithExtensionRegistry(proto2ExtensionRegistry)
                                                                .ParseFrom(request.ProtobufPayload),
                            _ => throw new Exception($" Protobuf request doesn't have specific payload type ({request.MessageType})"),
                        };
                        break;
					case ConformanceRequest.PayloadOneofCase.TextPayload:
						return new ConformanceResponse { Skipped = "CSharp doesn't support text format" };
                    default:
                        throw new Exception("Unsupported request payload: " + request.PayloadCase);
                }
            }
            catch (InvalidProtocolBufferException e)
            {
                return new ConformanceResponse { ParseError = e.Message };
            }
            catch (InvalidJsonException e)
            {
                return new ConformanceResponse { ParseError = e.Message };
            }
            try
            {
                switch (request.RequestedOutputFormat)
                {
                    case global::Conformance.WireFormat.Json:
                        var formatter = new JsonFormatter(new JsonFormatter.Settings(false, typeRegistry));
                        return new ConformanceResponse { JsonPayload = formatter.Format(message) };
                    case global::Conformance.WireFormat.Protobuf:
                        return new ConformanceResponse { ProtobufPayload = message.ToByteString() };
                    default:
                        throw new Exception("Unsupported request output format: " + request.RequestedOutputFormat);
                }
            }
            catch (InvalidOperationException e)
            {
                return new ConformanceResponse { SerializeError = e.Message };
            }
        }

        private static int? ReadInt32(BinaryReader input)
        {
            byte[] bytes = input.ReadBytes(4);
            if (bytes.Length == 0)
            {
                // Cleanly reached the end of the stream
                return null;
            }
            if (bytes.Length != 4)
            {
                throw new EndOfStreamException("Read " + bytes.Length + " bytes of size when expecting 4");
            }
            return bytes[0] | (bytes[1] << 8) | (bytes[2] << 16) | (bytes[3] << 24);
        }
    }
}
