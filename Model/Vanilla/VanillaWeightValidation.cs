namespace _4RTools.Model.Vanilla
{
    // Raw client units; discovery must establish their scale before a mapping is verified.
    internal static class VanillaWeightValidation
    {
        internal const uint MaximumRawWeight = 1000000000U;
        internal const uint ExpectedCartMaximum = 10000U;

        internal static string PairError(uint current, uint maximum)
        {
            if (maximum == 0) return "Weight maximum must be greater than zero.";
            if (maximum > MaximumRawWeight) return "Weight maximum exceeds the sanity limit of 1000000000 raw units.";
            if (current > maximum) return "Current weight exceeds maximum weight.";
            return null;
        }

        internal static string CartPairError(uint current, uint maximum)
        {
            string error = PairError(current, maximum);
            if (error != null) return error;
            if (maximum != ExpectedCartMaximum)
                return "Cart maximum must equal the verified Vanilla cart capacity of 10000.";
            return null;
        }

        internal static void ValidateSample(VanillaClientState state)
        {
            if (state == null) return;
            ValidatePair(state.CurrentWeight, state.MaxWeight, false);
            ValidatePair(state.CurrentCartWeight, state.MaxCartWeight, true);
        }

        private static void ValidatePair(StateValue<uint> current, StateValue<uint> maximum, bool cart)
        {
            if (!current.IsAvailable || !maximum.IsAvailable)
            {
                StateValue available = current.IsAvailable ? current : maximum;
                if (available.IsAvailable)
                {
                    available.Validation = StateValidation.Invalid;
                    available.Error = (cart ? "Cart weight" : "Weight") + " pair is incomplete: "
                        + (current.IsAvailable ? (cart ? "MaxCartWeight" : "MaxWeight") : (cart ? "CurrentCartWeight" : "CurrentWeight"))
                        + " is unavailable.";
                }
                return;
            }
            string error = cart ? CartPairError(current.Value, maximum.Value) : PairError(current.Value, maximum.Value);
            if (error == null) return;
            current.Validation = maximum.Validation = StateValidation.Invalid;
            current.Error = maximum.Error = error;
        }

        internal static bool TryGetPercent(VanillaClientState state, out decimal percent, out string error)
        {
            return TryGetPercent(state, false, out percent, out error);
        }

        internal static bool TryGetCartPercent(VanillaClientState state, out decimal percent, out string error)
        {
            return TryGetPercent(state, true, out percent, out error);
        }

        private static bool TryGetPercent(VanillaClientState state, bool cart, out decimal percent, out string error)
        {
            percent = 0;
            if (state == null) { error = "No weight sample is available."; return false; }
            if (state.Error != null) { error = state.Error; return false; }
            StateValue<uint> current = cart ? state.CurrentCartWeight : state.CurrentWeight;
            StateValue<uint> maximum = cart ? state.MaxCartWeight : state.MaxWeight;
            if (!current.IsAvailable || !maximum.IsAvailable
                || current.Validation != StateValidation.Valid || maximum.Validation != StateValidation.Valid)
            {
                error = current.Error ?? maximum.Error
                    ?? (cart ? "Cart weight fields are not verified and valid for the current sample."
                             : "Weight fields are not verified and valid for the current sample.");
                return false;
            }
            error = cart ? CartPairError(current.Value, maximum.Value) : PairError(current.Value, maximum.Value);
            if (error != null) return false;
            percent = current.Value * 100m / maximum.Value;
            return true;
        }
    }
}
