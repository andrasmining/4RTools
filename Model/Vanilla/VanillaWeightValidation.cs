namespace _4RTools.Model.Vanilla
{
    // Raw client units; discovery must establish their scale before a mapping is verified.
    internal static class VanillaWeightValidation
    {
        internal const uint MaximumRawWeight = 1000000000U;

        internal static string PairError(uint current, uint maximum)
        {
            if (maximum == 0) return "Weight maximum must be greater than zero.";
            if (maximum > MaximumRawWeight) return "Weight maximum exceeds the sanity limit of 1000000000 raw units.";
            if (current > maximum) return "Current weight exceeds maximum weight.";
            return null;
        }

        internal static void ValidateSample(VanillaClientState state)
        {
            if (!state.CurrentWeight.IsAvailable || !state.MaxWeight.IsAvailable)
            {
                StateValue available = state.CurrentWeight.IsAvailable ? state.CurrentWeight : state.MaxWeight;
                if (available.IsAvailable)
                {
                    available.Validation = StateValidation.Invalid;
                    available.Error = "Weight pair is incomplete: "
                        + (state.CurrentWeight.IsAvailable ? "MaxWeight" : "CurrentWeight") + " is unavailable.";
                }
                return;
            }
            string error = PairError(state.CurrentWeight.Value, state.MaxWeight.Value);
            if (error == null) return;
            state.CurrentWeight.Validation = state.MaxWeight.Validation = StateValidation.Invalid;
            state.CurrentWeight.Error = state.MaxWeight.Error = error;
        }

        internal static bool TryGetPercent(VanillaClientState state, out decimal percent, out string error)
        {
            percent = 0;
            if (state == null) { error = "No weight sample is available."; return false; }
            if (state.Error != null) { error = state.Error; return false; }
            if (!state.CurrentWeight.IsAvailable || !state.MaxWeight.IsAvailable
                || state.CurrentWeight.Validation != StateValidation.Valid || state.MaxWeight.Validation != StateValidation.Valid)
            {
                error = state.CurrentWeight.Error ?? state.MaxWeight.Error ?? "Weight fields are not verified and valid for the current sample.";
                return false;
            }
            error = PairError(state.CurrentWeight.Value, state.MaxWeight.Value);
            if (error != null) return false;
            percent = state.CurrentWeight.Value * 100m / state.MaxWeight.Value;
            return true;
        }
    }
}
