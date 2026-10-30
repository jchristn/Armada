namespace Armada.Core.Enums
{
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;

    /// <summary>
    /// A kind of automatic recovery Armada attempted before a black box failure (handoff and landing trail).
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BlackBoxAutoRecoveryKindEnum
    {
        /// <summary>
        /// The mission was redispatched to a captain.
        /// </summary>
        [EnumMember(Value = "Redispatch")]
        Redispatch,

        /// <summary>
        /// The captain was asked to repair its output contract (markers, sections, plan block).
        /// </summary>
        [EnumMember(Value = "OutputRepair")]
        OutputRepair,

        /// <summary>
        /// The landing was retried (automatically or by the landing integrity sweep).
        /// </summary>
        [EnumMember(Value = "LandingRetry")]
        LandingRetry,

        /// <summary>
        /// A deferred landing (dirty checkout) was retried.
        /// </summary>
        [EnumMember(Value = "DeferralRetry")]
        DeferralRetry,

        /// <summary>
        /// A rescue mission was launched.
        /// </summary>
        [EnumMember(Value = "Rescue")]
        Rescue,

        /// <summary>
        /// The dock was repaired, unstuck or its worktree restored.
        /// </summary>
        [EnumMember(Value = "DockRepair")]
        DockRepair,

        /// <summary>
        /// The landing was reconciled with the target (already landed, pull request merged).
        /// </summary>
        [EnumMember(Value = "Reconcile")]
        Reconcile,

        /// <summary>
        /// An orphaned captain exit was recovered.
        /// </summary>
        [EnumMember(Value = "OrphanRecovery")]
        OrphanRecovery,

        /// <summary>
        /// A value this build does not recognize (written by a newer Armada); tolerant readers map unknown values here.
        /// </summary>
        [EnumMember(Value = "Unknown")]
        Unknown
    }
}
