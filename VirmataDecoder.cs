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
		[Input("FirmataMessage")]
		IDiffSpread<String> ansiMessage;
		
		[Input("AnalogInputCount",DefaultValue = 6, Visibility = PinVisibility.OnlyInspector)]
		ISpread<int> analogInputCount;
		
		[Input("DigitalInputCount",DefaultValue = 12, Visibility = PinVisibility.OnlyInspector)]
		ISpread<int> digitalInputCount;
		
		[Output("AnalogIn")]
		ISpread<int> analogIns;
		
		[Output("DigitalIn")]
		ISpread<int> digitalIns;
		
		[Import()]
		ILogger FLogger;
		#endregion fields & pins
		
		//called when data for any output pin is requested
		public void Evaluate(int SpreadMax)
		{
			
			int numberOfAnalogs = 6;
			analogIns.SliceCount = numberOfAnalogs;
			
		
			if(ansiMessage.SliceCount>0 && ansiMessage.IsChanged == true )
			{
				byte[] byteMessage = new byte[numberOfAnalogs];
				byteMessage = Encoding.GetEncoding(1252).GetBytes(ansiMessage[0]);
				string fullStringMessage = PrepareMessage(byteMessage,numberOfAnalogs);
				GetSetAnalogIns(fullStringMessage,numberOfAnalogs);	
				
			}
			
			
		}
		
		
		// Helper Functions
		
		
		static int GetValueFromBytes(byte MSB, byte LSB)
		{
			int tempValue = MSB & 0x7F;
			tempValue = tempValue << 7;
			tempValue = tempValue | (LSB & 0x7F);
			return tempValue;
		}
		
		static string PrepareMessage(byte[] byteMessage, int MessageLenght)
		{
			
			// decoding to int16 as string like E0 E1 for analog pins ID
			string[] stringMessage = new string[byteMessage.Length];
			for (int i = 0; i < byteMessage.Length; i++) stringMessage[i] = byteMessage[i].ToString("X2");
			string fullStringMessage = string.Join(".", stringMessage);
			return fullStringMessage;
		}
		
		void GetSetAnalogIns(string Message, int NumberOfAnalogs)
		{
			for (int i = 0; i < NumberOfAnalogs; i++)
			{
				
				int firstCharacter = Message.IndexOf("E" + Convert.ToString(i),0);
				int checkLenght = Message.Length - firstCharacter;	
			
				if (checkLenght > 12 && firstCharacter != -1){
				
				string MSB = Message.Substring(firstCharacter+3,2);	
				string LSB = Message.Substring(firstCharacter+6,2);
					
				int analogValues = GetValueFromBytes(Convert.ToByte(LSB,16),Convert.ToByte(MSB,16));	
				analogIns[i] = analogValues;
				}
				
			}
		}
		
		
	}
	
	
}
