#region usings
using System;
using System.ComponentModel.Composition;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Utils.VColor;
using VVVV.Utils.VMath;

using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
	#region PluginInfo
	[PluginInfo(Name = "FirmataDecode",
	            Category = "Devices",
	            Help = "decodes the firmata2.2 protocol",
	            Tags = "")]
	#endregion PluginInfo
	public class FirmataDecode : IPluginEvaluate
	{
		#region fields & pins
		[Input("FirmataMessage", DefaultValue = 1.0)]
		ISpread<String> FInput;

		[Output("Output")]
		ISpread<double> FOutput;

		[Import()]
		ILogger FLogger;
		#endregion fields & pins
 
		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{
			FOutput.SliceCount = SpreadMax;

			for (int i = 0; i < SpreadMax; i++)
				FOutput[i] = 1;
				 
			//FLogger.Log(LogType.Debug, "hi tty!");
		}
	}
}
