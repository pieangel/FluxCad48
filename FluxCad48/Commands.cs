using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.Runtime;

namespace FluxCad48
{
	public class Commands
	{
		[CommandMethod("FLUX_HELLO")]
		public void FluxHello()
		{
			Document doc = Application.DocumentManager.MdiActiveDocument;
			Editor ed = doc.Editor;

			ed.WriteMessage("\n[FluxCad48] Hello BricsCAD .NET Framework 4.8 Plugin!");
		}
	}
}