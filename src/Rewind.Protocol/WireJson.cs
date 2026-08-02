using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Rewind.Abstractions;

namespace Rewind.Protocol;

public static class WireJson
{
    public static byte[] SerializeHello(WireMessage envelope)
    {
        return EncodeEnvelope(envelope, "{}");
    }

    public static byte[] SerializeTrigger(WireMessage envelope, string name, string details)
    {
        string payload = "{\"name\":" + Quote(name) + ",\"details\":" + Quote(details) + "}";
        return EncodeEnvelope(envelope, payload);
    }

    public static byte[] SerializeEvent(WireMessage envelope, RewindEvent value)
    {
        var context = new StringBuilder("{");
        bool first = true;
        foreach (KeyValuePair<string, string> item in value.Context)
        {
            if (!first)
            {
                context.Append(',');
            }

            first = false;
            context.Append(Quote(item.Key)).Append(':').Append(Quote(item.Value));
        }

        context.Append('}');
        string payload = "{"
            + "\"eventId\":" + Quote(value.EventId.ToString("D")) + ","
            + "\"timestampUtc\":" + Quote(value.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)) + ","
            + "\"clientSequence\":" + value.ClientSequence.ToString(CultureInfo.InvariantCulture) + ","
            + "\"level\":" + Quote(value.Level.ToString()) + ","
            + "\"source\":" + Quote(value.Source) + ","
            + "\"name\":" + Quote(value.Name) + ","
            + "\"message\":" + Quote(value.Message) + ","
            + "\"context\":" + context + ","
            + "\"processId\":" + value.ProcessId.ToString(CultureInfo.InvariantCulture) + ","
            + "\"threadId\":" + value.ThreadId.ToString(CultureInfo.InvariantCulture)
            + "}";
        return EncodeEnvelope(envelope, payload);
    }

    private static byte[] EncodeEnvelope(WireMessage value, string payloadJson)
    {
        string json = "{"
            + "\"protocolVersion\":" + value.ProtocolVersion.ToString(CultureInfo.InvariantCulture) + ","
            + "\"type\":" + Quote(value.Type.ToString()) + ","
            + "\"clientInstanceId\":" + Quote(value.ClientInstanceId.ToString("D")) + ","
            + "\"messageId\":" + Quote(value.MessageId.ToString("D")) + ","
            + "\"clientSequence\":" + value.ClientSequence.ToString(CultureInfo.InvariantCulture) + ","
            + "\"payload\":" + payloadJson
            + "}";
        return Encoding.UTF8.GetBytes(json);
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder(value.Length + 2).Append('"');
        foreach (char character in value)
        {
            switch (character)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\b': result.Append("\\b"); break;
                case '\f': result.Append("\\f"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                default:
                    if (character < ' ')
                    {
                        result.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        result.Append(character);
                    }

                    break;
            }
        }

        return result.Append('"').ToString();
    }
}
