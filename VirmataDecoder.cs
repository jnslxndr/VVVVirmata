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
		IDiffSpread<String> firmataMessage;
		
		[Output("AnalogIn")]
		ISpread<String> analogIns;
		
		[Output("DigitalIn")]
		ISpread<int> digitalIns;
		
		[Import()]
		ILogger FLogger;
		#endregion fields & pins
		
		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{
			
			
			
			if(firmataMessage.SliceCount>0 && firmataMessage.IsChanged == true )
			{
				byte[] ba = Encoding.GetEncoding(1252).GetBytes(firmataMessage[0]);
				//FLogger.Log(LogType.Debug,Convert.ToString(ba[0]));
				//byte[] ba = System.Text.Encoding.ASCII.GetBytes(firmataMessage[0]);
				analogIns.SliceCount = ba.Length;
				
				
				for (int i = 0; i < ba.Length; i++)
				analogIns[i] = Convert.ToString(ba[i],16);
			
				int test = GetValueFromBytes(01,50);
				digitalIns[0]=test;
				
			}
			
			
			
			
		
			
	
			
			
			
			//FLogger.Log(LogType.Debug, "hi tty!");
		}
		
		
		// Helper Functions
		
		
		static int GetValueFromBytes(byte MSB, byte LSB)
		{
			int tempValue = MSB & 0x7F;
			tempValue = tempValue << 7;
			tempValue = tempValue | (LSB & 0x7F);
			return tempValue;
		}
		
	}
	
	
}
