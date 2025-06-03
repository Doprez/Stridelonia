using Stride.Engine;

namespace Stridelonia
{
    public class StrideloniaGame : Game
    {
        protected override void BeginRun()
        {
            base.BeginRun();
            //breaks input when resized
            //Window.AllowUserResizing = true;
            StrideloniaApplication.Start(this);
        }

        protected override void EndRun()
        {
            base.EndRun();
            StrideloniaApplication.Stop();
        }
    }
}
