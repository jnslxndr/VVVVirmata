#region usings
using System;
using System.ComponentModel.Composition;
using System.Collections.Generic;
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
	[PluginInfo(Name = "VirmataEncoder",
	Category = "String",
	Version = "0.1",
	Help = "Encodes Messages for Firmata (Protocol v2.2)",
	Tags = "String")]
	#endregion PluginInfo
	
	public class VirmataEncoder : IPluginEvaluate
	{
		///
		/// INPUT
		///
		[Input("values")]
		IDiffSpread<bool> PinValues;
		
		[Input("report analog pins")]
		IDiffSpread<bool> ReportAnalogPins;
		
		
		[Input("ReportDigitalPins")]
		IDiffSpread<bool> ReportDigitalPins;
		
		//// Use a default SamplingRate of 40ms
		[Input("Samplerate", MinValue = 0, DefaultValue = 40)]
		IDiffSpread<int> Samplerate;
		
		
		[Input("ReportFirmwareVersion")]
		IDiffSpread<bool> ReportFirmwareVersion;
		
		[Input("ResetSystem")]
		IDiffSpread<bool> ResetSystem;
		
		
		
		
		
		
		///
		/// OUTPUT
		///
		[Output("Firmatamessage")]
		ISpread<string> FirmataOut;
		
		[Output("change")]
		ISpread<bool> ChangedOut;
		
		[Import()]
		ILogger FLogger;
		
		public void Evaluate(int SpreadMax)
		{
			string command_out = "";
			if (ReportAnalogPins.IsChanged)
			{
				// TODO: It should not be a fixed number of pins, later versions
				command_out += SetAnaPinReportingForRange(6,ReportAnalogPins[0]);
			}
			
			if (ReportDigitalPins.IsChanged)
			{
				// TODO: Check which pin number should be reported and enable only the proper port.
				command_out += GetDigitalPinReportingCommandForState(ReportDigitalPins[0],ATMegaPorts.PORTB);
				command_out += GetDigitalPinReportingCommandForState(ReportDigitalPins[0],ATMegaPorts.PORTC);
				command_out += GetDigitalPinReportingCommandForState(ReportDigitalPins[0],ATMegaPorts.PORTD);
			}
			
			if(Samplerate.IsChanged)
			{
				if (ReportAnalogPins[0])
					command_out += SetAnaPinReportingForRange(6,false);
				
				command_out += GetSamplerateCommand(Samplerate[0]);
				
				if (ReportAnalogPins[0])
					command_out += SetAnaPinReportingForRange(6,ReportAnalogPins[0]);
			}
			
			if(ResetSystem.IsChanged)
			command_out += GetResetCommand();
			
			if (ReportFirmwareVersion.IsChanged)
			command_out += GetFirmwareVersionCommand();
			
			/*
Did something change at all?
*/
			bool something_changed = (command_out!="");
			ChangedOut[0] = something_changed;
			if(something_changed) FirmataOut[0] = command_out;
		}
		
		
		#region Helper functions
		
		
		/* This is a shortcut to encode byte arrays, which also contain bytes higer than 127 */
		static string Encode(byte[] bytes) {return Encoding.GetEncoding("Latin1").GetString(bytes);}
		
		
		static string GetSamplerateCommand(int rate)
		{
			byte lsb,msb;
			GetBytesFromValue(rate,out msb,out lsb);
			
			byte[] cmd = {
				FirmataCommands.SYSEX_START,
				FirmataCommands.SAMPLING_INTERVAL,
				lsb,msb,
				FirmataCommands.SYSEX_END
			};
			return Encode(cmd);
		}
		
		
		/* Query Firmware Name and Version
* 0  START_SYSEX (0xF0)
* 1  queryFirmware (0x79)
* 2  END_SYSEX (0xF7)
*/
		static string GetFirmwareVersionCommand()
		{
			byte[] cmd = {
				FirmataCommands.SYSEX_START,
				FirmataCommands.REPORT_FIRMWARE_VERSION_NUM,
				FirmataCommands.SYSEX_END
			};
			return Encode(cmd);
		}
		
		
		static string GetAnalogPinReportingCommandForState(bool state,int pin)
		{
			byte val,command;
			val = (byte) (state ? 0x01 : 0x00);
			command = (byte)(FirmataCommands.TOGGLEANALOGREPORT|pin);
			byte[] cmd = {command, val};
			return Encode(cmd);
		}
		
		static string SetAnaPinReportingForRange(int range, bool state)
		{
			string command_out = "";
			for(int i = 0; i<range; i++)
			command_out += GetAnalogPinReportingCommandForState(state,i);
			return command_out;
		}
		
		
		
		
		static string GetDigitalPinReportingCommandForState(bool state,int port)
		{
			byte val,command;
			val = (byte) (state ? 0x01 : 0x00);
			command = (byte)(FirmataCommands.TOGGLEDIGITALREPORT|port);
			byte[] cmd = {command, val};
			return Encode(cmd);
		}
		
		
		static string GetResetCommand()
		{
			byte[] cmd = {
				FirmataCommands.SYSEX_START,
				FirmataCommands.RESET,
				FirmataCommands.SYSEX_END
			};
			return Encode(cmd);
		}
		
		
		/// <summary>
		/// Get the integer value that was sent using the 7-bit messages of the firmata protocol
		/// </summary>
		static int GetValueFromBytes(byte MSB, byte LSB)
		{
			int tempValue = MSB & 0x7F;
			tempValue = tempValue << 7;
			tempValue = tempValue | (LSB & 0x7F);
			return tempValue;
		}
		
		/// <summary>
		/// Split an integer value to two 7-bit parts so it can be sent using the firmata protocol
		/// </summary>
		static void GetBytesFromValue(int value, out byte MSB, out byte LSB)
		{
			LSB = (byte)(value & 0x7F);
			MSB = (byte)((value >> 7) & 0x7F);
		}
		#endregion
		
	}
	
	public enum PinModes
	{
		INPUT,
		OUTPUT,
		ANALOG,
		PWM,
		SERVO
	}
	
	#region DEFINITIONS
	public static class FirmataCommands
	{
		/// <summary>
		/// The command that toggles the continuous sending of the
		/// analog reading of the specified pin
		/// </summary>
		public const byte TOGGLEANALOGREPORT = 0xC0;
		/// <summary>
		/// The distinctive value that states that this message is an analog message.
		/// It comes as a report for analog in pins, or as a command for PWM
		/// </summary>
		public const byte ANALOGMESSAGE = 0xE0;
		/// <summary>
		/// The command that toggles the continuous sending of the
		/// digital state of the specified port
		/// </summary>
		public const byte TOGGLEDIGITALREPORT = 0xD0;
		/// <summary>
		/// The distinctive value that states that this message is a digital message.
		/// It comes as a report or as a command
		/// </summary>
		public const byte DIGITALMESSAGE = 0x90;
		/// <summary>
		/// A command to change the pin mode for the specified pin
		/// </summary>
		public const byte SETPINMODE = 0xF4;
		
		public const byte SYSEX_START = 0xF0;
		public const byte SYSEX_END = 0xF7;
		
		public const byte REPORT_FIRMWARE_VERSION_NUM = 0x79;
		
		public const byte RESET = 0xFF;
		
		public const byte SAMPLING_INTERVAL = 0x7A;
	}
	
	public static class PinModeBytes
	{
		public const byte INPUT = 0x00;
		public const byte OUTPUT = 0x01;
		/// <summary>
		/// This is not implemented in the standard firmata program
		/// </summary>
		public const byte ANALOG = 0x02;
		public const byte PWM = 0x03;
		/// <summary>
		/// This is not implemented in the standard firmata program
		/// </summary>
		public const byte SERVO = 0x04;
		
	}
	
	public static class ATMegaPorts
	{
		/// <summary>
		/// This port represents digital pins 8..13. 14 and 15 are for the crystal
		/// </summary>
		public const byte PORTB = 1;
		
		/// <summary>
		/// This port represents analog input pins 0..5
		/// </summary>
		public const byte PORTC = 2;
		
		/// <summary>
		/// This port represents digital pins 0..7. Pins 0 and 1 are reserved for communication
		/// </summary>
		public const byte PORTD = 0;
	}
	
	#endregion
	
	
}
