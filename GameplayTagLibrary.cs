namespace RadioDecadance.GameplayTags
{
    /// <summary>
    /// Centralized, code-first gameplay tags you can reference from code without magic strings.
    /// These default to FNV-1a ids computed from the full tag path so they are stable across sessions.
    /// Make sure corresponding names exist in GameplayTagConfig for nicer editor display and hierarchy features.
    /// </summary>
    public static class GameplayTagLibrary
    {
        /// <summary>
        /// Standardized reasons for why adding an effect was denied.
        /// </summary>
        public static class EffectApplyFailReasons
        {
            /// <summary>
            /// Reached configured maximum number of stacks for an effect tag.
            /// Suggested name in config: "Effect.FailReason.MaxStacksReached"
            /// </summary>
            [AutoGenerateTag]
            public static readonly GameplayTag MaxStacksReached = GameplayTag.FromString("Effect.FailReason.MaxStacksReached");

            /// <summary>
            /// Effect's internal pre-apply validation failed (requirements not met).
            /// Suggested name in config: "Effect.FailReason.FailedInternalCheck"
            /// </summary>
            [AutoGenerateTag]
            public static readonly GameplayTag FailedInternalCheck = GameplayTag.FromString("Effect.FailReason.FailedInternalCheck");

            /// <summary>
            /// A duplicate effect with the same tag was discarded due to policy = None.
            /// Suggested name in config: "Effect.FailReason.DuplicateDiscarded"
            /// </summary>
            [AutoGenerateTag]
            public static readonly GameplayTag DuplicateDiscarded = GameplayTag.FromString("Effect.FailReason.DuplicateDiscarded");

            /// <summary>
            /// A configured opposite tag was found on the target; a stack of it was removed instead of applying this effect.
            /// Suggested name in config: "Effect.FailReason.OppositeTagConsumed"
            /// </summary>
            [AutoGenerateTag]
            public static readonly GameplayTag OppositeTagConsumed = GameplayTag.FromString("Effect.FailReason.OppositeTagConsumed");
        }
    }
}
