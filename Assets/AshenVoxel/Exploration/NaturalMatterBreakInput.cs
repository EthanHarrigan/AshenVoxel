namespace AshenVoxel.Exploration.Experimental
{
    public enum NaturalMatterBreakIntent { None, Attempt, Waiting }

    /// <summary>One live hand gesture, shared by tapping and held repeats.
    /// No stored ray or released click can become a later edit.</summary>
    public sealed class NaturalMatterBreakInput
    {
        public const double RepeatSeconds=.15;
        bool gesture;
        double nextRepeat;

        public void Reset(){gesture=false;nextRepeat=0;}

        public NaturalMatterBreakIntent Poll(bool pressed,bool held,bool enabled,bool ready,double now)
        {
            if(!enabled||double.IsNaN(now)||double.IsInfinity(now)){Reset();return NaturalMatterBreakIntent.None;}
            if(pressed)gesture=true;
            if(!pressed&&!held){Reset();return NaturalMatterBreakIntent.None;}
            if(!gesture)return NaturalMatterBreakIntent.None;
            // A complete tap may press and release within one input update.
            if(!held)gesture=false;
            if(!ready)return NaturalMatterBreakIntent.Waiting;
            if(!pressed&&now<nextRepeat)return NaturalMatterBreakIntent.None;
            nextRepeat=now+RepeatSeconds;
            return NaturalMatterBreakIntent.Attempt;
        }
    }
}
