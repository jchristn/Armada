namespace Armada.Test.Unit.Suites.Models
{
    using System.Text.Json;
    using Armada.Core.Enums;
    using Armada.Test.Common;

    public class EnumModelTests : TestSuite
    {
        public override string Name => "Enum Model";

        protected override async Task RunTestsAsync()
        {
            // MissionStatusEnum
            await RunTest("MissionStatusEnum Pending SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(MissionStatusEnum.Pending);
                AssertEqual("\"Pending\"", json);
                MissionStatusEnum deserialized = JsonSerializer.Deserialize<MissionStatusEnum>(json);
                AssertEqual(MissionStatusEnum.Pending, deserialized);
            });

            await RunTest("MissionStatusEnum Assigned SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(MissionStatusEnum.Assigned);
                AssertEqual("\"Assigned\"", json);
                MissionStatusEnum deserialized = JsonSerializer.Deserialize<MissionStatusEnum>(json);
                AssertEqual(MissionStatusEnum.Assigned, deserialized);
            });

            await RunTest("MissionStatusEnum InProgress SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(MissionStatusEnum.InProgress);
                AssertEqual("\"InProgress\"", json);
                MissionStatusEnum deserialized = JsonSerializer.Deserialize<MissionStatusEnum>(json);
                AssertEqual(MissionStatusEnum.InProgress, deserialized);
            });

            await RunTest("MissionStatusEnum Testing SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(MissionStatusEnum.Testing);
                AssertEqual("\"Testing\"", json);
                MissionStatusEnum deserialized = JsonSerializer.Deserialize<MissionStatusEnum>(json);
                AssertEqual(MissionStatusEnum.Testing, deserialized);
            });

            await RunTest("MissionStatusEnum Review SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(MissionStatusEnum.Review);
                AssertEqual("\"Review\"", json);
                MissionStatusEnum deserialized = JsonSerializer.Deserialize<MissionStatusEnum>(json);
                AssertEqual(MissionStatusEnum.Review, deserialized);
            });

            await RunTest("MissionStatusEnum Complete SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(MissionStatusEnum.Complete);
                AssertEqual("\"Complete\"", json);
                MissionStatusEnum deserialized = JsonSerializer.Deserialize<MissionStatusEnum>(json);
                AssertEqual(MissionStatusEnum.Complete, deserialized);
            });

            await RunTest("MissionStatusEnum Failed SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(MissionStatusEnum.Failed);
                AssertEqual("\"Failed\"", json);
                MissionStatusEnum deserialized = JsonSerializer.Deserialize<MissionStatusEnum>(json);
                AssertEqual(MissionStatusEnum.Failed, deserialized);
            });

            await RunTest("MissionStatusEnum Cancelled SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(MissionStatusEnum.Cancelled);
                AssertEqual("\"Cancelled\"", json);
                MissionStatusEnum deserialized = JsonSerializer.Deserialize<MissionStatusEnum>(json);
                AssertEqual(MissionStatusEnum.Cancelled, deserialized);
            });

            // CaptainStateEnum
            await RunTest("CaptainStateEnum Idle SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(CaptainStateEnum.Idle);
                AssertEqual("\"Idle\"", json);
                CaptainStateEnum deserialized = JsonSerializer.Deserialize<CaptainStateEnum>(json);
                AssertEqual(CaptainStateEnum.Idle, deserialized);
            });

            await RunTest("CaptainStateEnum Working SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(CaptainStateEnum.Working);
                AssertEqual("\"Working\"", json);
                CaptainStateEnum deserialized = JsonSerializer.Deserialize<CaptainStateEnum>(json);
                AssertEqual(CaptainStateEnum.Working, deserialized);
            });

            await RunTest("CaptainStateEnum Stalled SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(CaptainStateEnum.Stalled);
                AssertEqual("\"Stalled\"", json);
                CaptainStateEnum deserialized = JsonSerializer.Deserialize<CaptainStateEnum>(json);
                AssertEqual(CaptainStateEnum.Stalled, deserialized);
            });

            await RunTest("CaptainStateEnum Stopping SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(CaptainStateEnum.Stopping);
                AssertEqual("\"Stopping\"", json);
                CaptainStateEnum deserialized = JsonSerializer.Deserialize<CaptainStateEnum>(json);
                AssertEqual(CaptainStateEnum.Stopping, deserialized);
            });

            // SignalTypeEnum
            await RunTest("SignalTypeEnum Assignment SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(SignalTypeEnum.Assignment);
                AssertEqual("\"Assignment\"", json);
                SignalTypeEnum deserialized = JsonSerializer.Deserialize<SignalTypeEnum>(json);
                AssertEqual(SignalTypeEnum.Assignment, deserialized);
            });

            await RunTest("SignalTypeEnum Progress SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(SignalTypeEnum.Progress);
                AssertEqual("\"Progress\"", json);
                SignalTypeEnum deserialized = JsonSerializer.Deserialize<SignalTypeEnum>(json);
                AssertEqual(SignalTypeEnum.Progress, deserialized);
            });

            await RunTest("SignalTypeEnum Completion SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(SignalTypeEnum.Completion);
                AssertEqual("\"Completion\"", json);
                SignalTypeEnum deserialized = JsonSerializer.Deserialize<SignalTypeEnum>(json);
                AssertEqual(SignalTypeEnum.Completion, deserialized);
            });

            await RunTest("SignalTypeEnum Error SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(SignalTypeEnum.Error);
                AssertEqual("\"Error\"", json);
                SignalTypeEnum deserialized = JsonSerializer.Deserialize<SignalTypeEnum>(json);
                AssertEqual(SignalTypeEnum.Error, deserialized);
            });

            await RunTest("SignalTypeEnum Heartbeat SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(SignalTypeEnum.Heartbeat);
                AssertEqual("\"Heartbeat\"", json);
                SignalTypeEnum deserialized = JsonSerializer.Deserialize<SignalTypeEnum>(json);
                AssertEqual(SignalTypeEnum.Heartbeat, deserialized);
            });

            await RunTest("SignalTypeEnum Nudge SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(SignalTypeEnum.Nudge);
                AssertEqual("\"Nudge\"", json);
                SignalTypeEnum deserialized = JsonSerializer.Deserialize<SignalTypeEnum>(json);
                AssertEqual(SignalTypeEnum.Nudge, deserialized);
            });

            await RunTest("SignalTypeEnum Mail SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(SignalTypeEnum.Mail);
                AssertEqual("\"Mail\"", json);
                SignalTypeEnum deserialized = JsonSerializer.Deserialize<SignalTypeEnum>(json);
                AssertEqual(SignalTypeEnum.Mail, deserialized);
            });

            // AgentRuntimeEnum
            await RunTest("AgentRuntimeEnum ClaudeCode SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(AgentRuntimeEnum.ClaudeCode);
                AssertEqual("\"ClaudeCode\"", json);
                AgentRuntimeEnum deserialized = JsonSerializer.Deserialize<AgentRuntimeEnum>(json);
                AssertEqual(AgentRuntimeEnum.ClaudeCode, deserialized);
            });

            await RunTest("AgentRuntimeEnum Codex SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(AgentRuntimeEnum.Codex);
                AssertEqual("\"Codex\"", json);
                AgentRuntimeEnum deserialized = JsonSerializer.Deserialize<AgentRuntimeEnum>(json);
                AssertEqual(AgentRuntimeEnum.Codex, deserialized);
            });

            await RunTest("AgentRuntimeEnum Custom SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(AgentRuntimeEnum.Custom);
                AssertEqual("\"Custom\"", json);
                AgentRuntimeEnum deserialized = JsonSerializer.Deserialize<AgentRuntimeEnum>(json);
                AssertEqual(AgentRuntimeEnum.Custom, deserialized);
            });

            // VoyageStatusEnum
            await RunTest("VoyageStatusEnum Open SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(VoyageStatusEnum.Open);
                AssertEqual("\"Open\"", json);
                VoyageStatusEnum deserialized = JsonSerializer.Deserialize<VoyageStatusEnum>(json);
                AssertEqual(VoyageStatusEnum.Open, deserialized);
            });

            await RunTest("VoyageStatusEnum InProgress SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(VoyageStatusEnum.InProgress);
                AssertEqual("\"InProgress\"", json);
                VoyageStatusEnum deserialized = JsonSerializer.Deserialize<VoyageStatusEnum>(json);
                AssertEqual(VoyageStatusEnum.InProgress, deserialized);
            });

            await RunTest("VoyageStatusEnum Complete SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(VoyageStatusEnum.Complete);
                AssertEqual("\"Complete\"", json);
                VoyageStatusEnum deserialized = JsonSerializer.Deserialize<VoyageStatusEnum>(json);
                AssertEqual(VoyageStatusEnum.Complete, deserialized);
            });

            await RunTest("VoyageStatusEnum Failed SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(VoyageStatusEnum.Failed);
                AssertEqual("\"Failed\"", json);
                VoyageStatusEnum deserialized = JsonSerializer.Deserialize<VoyageStatusEnum>(json);
                AssertEqual(VoyageStatusEnum.Failed, deserialized);
            });

            await RunTest("VoyageStatusEnum Cancelled SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(VoyageStatusEnum.Cancelled);
                AssertEqual("\"Cancelled\"", json);
                VoyageStatusEnum deserialized = JsonSerializer.Deserialize<VoyageStatusEnum>(json);
                AssertEqual(VoyageStatusEnum.Cancelled, deserialized);
            });

            // PlaybookDeliveryModeEnum
            await RunTest("PlaybookDeliveryModeEnum InlineFullContent SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(PlaybookDeliveryModeEnum.InlineFullContent);
                AssertEqual("\"InlineFullContent\"", json);
                PlaybookDeliveryModeEnum deserialized = JsonSerializer.Deserialize<PlaybookDeliveryModeEnum>(json);
                AssertEqual(PlaybookDeliveryModeEnum.InlineFullContent, deserialized);
            });

            await RunTest("PlaybookDeliveryModeEnum InstructionWithReference SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(PlaybookDeliveryModeEnum.InstructionWithReference);
                AssertEqual("\"InstructionWithReference\"", json);
                PlaybookDeliveryModeEnum deserialized = JsonSerializer.Deserialize<PlaybookDeliveryModeEnum>(json);
                AssertEqual(PlaybookDeliveryModeEnum.InstructionWithReference, deserialized);
            });

            await RunTest("PlaybookDeliveryModeEnum AttachIntoWorktree SerializesAsString", () =>
            {
                string json = JsonSerializer.Serialize(PlaybookDeliveryModeEnum.AttachIntoWorktree);
                AssertEqual("\"AttachIntoWorktree\"", json);
                PlaybookDeliveryModeEnum deserialized = JsonSerializer.Deserialize<PlaybookDeliveryModeEnum>(json);
                AssertEqual(PlaybookDeliveryModeEnum.AttachIntoWorktree, deserialized);
            });
        }
    }
}
