#region usings
using System;
using System.ComponentModel.Composition;
using System.Text;
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
		ISpread<String> firmataMessage;
		
		[Output("AnalogIn")]
		ISpread<int> analogIns;
		
		[Output("DigitalIn")]
		ISpread<bool> digitalIns;
		
		[Import()]
		ILogger FLogger;
		#endregion fields & pins
		
		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{
			analogIns.SliceCount = SpreadMax;
			if(firmataMessage.SliceCount>0)
			{
				byte[] ba = Encoding.GetEncoding(1252).GetBytes(firmataMessage[0]);
				FLogger.Log(LogType.Debug,Encoding.GetEncoding(1252).GetString(ba));
			}
			
			//Encoding.GetEncoding(1252).GetString(bytes);
			
			//FLogger.Log(LogType.Debug, "hi tty!");
		}
	}
}
