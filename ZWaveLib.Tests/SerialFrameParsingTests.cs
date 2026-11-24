using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace ZWaveLib.Tests;

public class SerialFrameParsingTests
{
    private ZWaveController controller = null!;
    private string configFolder = null!;
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp]
    public void SetUp()
    {
        configFolder = Path.Combine(Path.GetTempPath(), "ZWaveLib.Tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(configFolder);
        controller = new ZWaveController();
        SetField("configFolder", configFolder);
    }

    [TearDown]
    public void TearDown()
    {
        controller?.Dispose();
        File.Delete(Path.Combine(configFolder, "zwavenodes.xml"));
        Directory.Delete(configFolder);
    }

    [Test]
    public void InitResponseSucceedsAtEverySplitPoint()
    {
        byte[] response = InitResponse();
        for (int split = 1; split < response.Length; split++)
        {
            ResetParser();
            Parse(response[..split]);
            Assert.That(GetField("currentStage"), Is.EqualTo(QueryStage.WaitAck), $"split {split}");
            Parse(response[split..]);
            Assert.That(GetField("currentStage"), Is.EqualTo(QueryStage.Complete), $"split {split}");
            Assert.That(GetField("serialBuffer"), Is.Null, $"split {split}");
        }
    }

    [Test]
    public void InitResponseSucceedsOneByteAtATimeAfterAck()
    {
        ResetParser();
        Parse(new byte[] { (byte)FrameHeader.ACK });
        foreach (byte value in InitResponse())
            Parse(new[] { value });
        Assert.That(GetField("currentStage"), Is.EqualTo(QueryStage.Complete));
        Assert.That(GetField("serialBuffer"), Is.Null);
    }

    [Test]
    public void AckAndFragmentedResponseInSameReadAreReassembled()
    {
        byte[] response = InitResponse();
        ResetParser();
        Parse(new byte[] { (byte)FrameHeader.ACK, response[0] });
        Assert.That(GetField("currentStage"), Is.EqualTo(QueryStage.WaitAck));
        Parse(response[1..]);
        Assert.That(GetField("currentStage"), Is.EqualTo(QueryStage.Complete));
    }

    [Test]
    public void ConsecutiveFramesAndControlBytesAreProcessed()
    {
        byte[] response = InitResponse();
        ResetParser();
        byte[] data = new byte[response.Length * 2 + 2];
        data[0] = (byte)FrameHeader.ACK;
        Array.Copy(response, 0, data, 1, response.Length);
        data[response.Length + 1] = (byte)FrameHeader.CAN;
        Array.Copy(response, 0, data, response.Length + 2, response.Length);
        Parse(data);
        Assert.That(GetField("currentStage"), Is.EqualTo(QueryStage.Error));
        Assert.That(GetField("serialBuffer"), Is.Null);
    }

    [Test]
    public void ValidFrameFollowingBadFrameIsProcessed()
    {
        byte[] response = InitResponse();
        byte[] data = new byte[response.Length * 2];
        Array.Copy(response, data, response.Length);
        data[response.Length - 1] ^= 1;
        Array.Copy(response, 0, data, response.Length, response.Length);
        ResetParser();
        Parse(data);
        Assert.That(GetField("currentStage"), Is.EqualTo(QueryStage.Complete));
    }

    [Test]
    public void InvalidLengthDoesNotBlockFollowingFrame()
    {
        byte[] response = InitResponse();
        byte[] data = new byte[response.Length + 2];
        data[0] = (byte)FrameHeader.SOF;
        data[1] = 0;
        Array.Copy(response, 0, data, 2, response.Length);
        ResetParser();
        Parse(data);
        Assert.That(GetField("currentStage"), Is.EqualTo(QueryStage.Complete));
    }

    [Test]
    public void BadChecksumDoesNotCompleteRequest()
    {
        byte[] response = InitResponse();
        response[^1] ^= 1;
        ResetParser();
        Parse(response);
        Assert.That(GetField("currentStage"), Is.EqualTo(QueryStage.WaitAck));
    }

    private static byte[] InitResponse()
    {
        byte[] response = new byte[39];
        response[0] = (byte)FrameHeader.SOF;
        response[1] = 37;
        response[2] = (byte)MessageType.Response;
        response[3] = (byte)ZWaveFunction.GetInitData;
        response[4] = 5;
        response[6] = 29;
        response[7] = 1;
        response[36] = 5;
        byte checksum = 0xff;
        for (int i = 1; i < response.Length - 1; i++)
            checksum ^= response[i];
        response[^1] = checksum;
        return response;
    }

    private void ResetParser()
    {
        SetField("serialBuffer", null);
        SetField("lastMessage", null);
        SetField("currentStage", QueryStage.WaitAck);
        SetField("pendingRequest", new ZWaveMessage(new byte[] { 1, 3, 0, (byte)ZWaveFunction.GetInitData, 0 }));
    }

    private void Parse(byte[] data) => typeof(ZWaveController).GetMethod("ParseSerialData", PrivateInstance)!.Invoke(controller, new object[] { data });
    private object? GetField(string name) => typeof(ZWaveController).GetField(name, PrivateInstance)!.GetValue(controller);
    private void SetField(string name, object? value) => typeof(ZWaveController).GetField(name, PrivateInstance)!.SetValue(controller, value);
}
