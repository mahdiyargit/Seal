namespace Seal
{
    public class SealPlugin : Rhino.PlugIns.PlugIn
    {
        public SealPlugin() => Instance = this;
        public static SealPlugin Instance { get; private set; }
    }
}