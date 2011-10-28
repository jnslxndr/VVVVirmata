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
	Help = "Encodes Pins,Values and Commands for Firmata (Protocol v2.2)",
	Tags = "String,Devices")]
	#endregion PluginInfo
	
	public class VirmataEncoder : IPluginEvaluate
	{
		///
		/// INPUT
		///
		[Input("values")]
		IDiffSpread<double> PinValues;
		
		[Input("report analog pins",IsSingle = true)]
		IDiffSpread<bool> ReportAnalogPins;
		
		[Input("ReportDigitalPins",IsSingle = true)]
		IDiffSpread<bool> ReportDigitalPins;
		
		//// Use a default SamplingRate of 40ms
		[Input("Samplerate", MinValue = 0, DefaultValue = 40,IsSingle = true)]
		IDiffSpread<int> Samplerate;
		
		[Input("ReportFirmwareVersion",IsSingle = true, Visibility = PinVisibility.Hidden)]
		IDiffSpread<bool> ReportFirmwareVersion;
		
		[Input("ResetSystem",IsSingle = true, Visibility = PinVisibility.Hidden)]
		IDiffSpread<bool> ResetSystem;
		
		[Input("PinModes", DefaultEnumEntry = "INPUT")]
		IDiffSpread<PinModes> PinModeSetup;
		
		[Input("SendOnCreate", Visibility = PinVisibility.Hidden, IsSingle = true, DefaultValue = 1)]
		IDiffSpread<bool> SendOnCreate;
		
		///
		/// OUTPUT
		///
		[Output("Firmatamessage")]
		ISpread<string> FirmataOut;
		
		[Output("Change")]
		ISpread<bool> ChangedOut;
		
		[Import()]
		ILogger FLogger;
		
		
		public void Evaluate(int SpreadMax)
		{
			string command_out = "";
			
			if(PinModeSetup.IsChanged)
			{
				for(int i=0; i<PinModeSetup.SliceCount; i++)
				{
					command_out += SetPinModeCommand(PinModeSetup[i],i);
				}
			}
			
			if (PinValues.IsChanged)
			{
				command_out += SetPinStates(PinValues);
			}
			
			/// Set Pinreporting for analog pins
			if (ReportAnalogPins.IsChanged)
			{
				// TODO: It should not be a fixed number of pins, later versions
				command_out += SetAnalogPinReportingForRange(6,ReportAnalogPins[0]);
			}
			
			/// Set Pinreporting for digital pins
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
				command_out += SetAnalogPinReportingForRange(6,false);
				
				command_out += GetSamplerateCommand(Samplerate[0]);
				
				if (ReportAnalogPins[0])
				command_out += SetAnalogPinReportingForRange(6,ReportAnalogPins[0]);
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
		static string Encode(byte[] bytes) {
			/// Use ANSI Encoding!
			return Encoding.GetEncoding(1252).GetString(bytes);
		}
		static string SetPinModeCommand(PinModes mode, int pin)
		{
			byte[] cmd = {
				FirmataCommands.SETPINMODE,
				(byte) pin,
				(byte) mode
			};
			return Encode(cmd);
		}
		
		static byte[] PinSpreadToPorts(ISpread<double> spread)
		{
			int num_ports = spread.SliceCount/8 + (spread.SliceCount%8==0 ? 0 : 1);
			byte[] bytes = new byte[num_ports];
			for(int port_index=0; port_index<num_ports; port_index++)
			{
				byte port = 0x00;
				for (int bit=0; bit<8; bit++)
				{
					int src_index = port_index*8+bit;
					double val = src_index<spread.SliceCount ? spread[src_index]:0;
					port |= (byte)((val >= 0.5 ? 1:0)<<bit);
				}
				/// TODO: Mask port with PWN PinsMask to 
				bytes[port_index] = port;
			}
			return bytes;
		}
		
		static string SetPinStates(ISpread<double> values)
		{
			// TODO: handle PWN set pins!
			
			byte[] ports = PinSpreadToPorts(values);
			List<byte> cmd = new List<byte>();
			for(int port=0; port<ports.Length; port++)
			{
				byte LSB, MSB;
				
				GetBytesFromValue(ports[port], out MSB, out LSB);
				
				byte the_port = ATMegaPorts.getPortForIndex(port);
				byte writeCommand = (byte)((uint) FirmataCommands.DIGITALMESSAGE | the_port);
								
				cmd.Add(writeCommand);
				cmd.Add(LSB);
				cmd.Add(MSB);
			}
			return Encode(cmd.ToArray());
		}
		
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
		
		
		/// Query Firmware Name and Version
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
		
		static string SetAnalogPinReportingForRange(int range, bool state)
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
		
		/// <summary>
		/// Send an array of boolean values indicating the state of each individual
		/// pin and get a byte representing a port
		/// </summary>
		public static byte GetPortFromPinValues(bool[] pins)
		{
			byte port = 0;
			for (int i = 0; i < pins.Length; i++)
			{
				port |= (byte) ((pins[i] ? 1 : 0) << i);
			}
			return port;
		}
		
		#endregion
		
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
	
		/// <summary>
		/// Sysex start command
		/// </summary>
		public const byte SYSEX_START = 0xF0;
		
		/// <summary>
		/// Sysex end command
		/// </summary>
		public const byte SYSEX_END = 0xF7;
		
		/// <summary>
		/// Report the Firmware version
		/// </summary>
		public const byte REPORT_FIRMWARE_VERSION_NUM = 0x79;
		
		/// <summary>
		/// Reset System Command
		/// </summary>
		public const byte RESET = 0xFF;
		
		/// <summary>
		/// Set Samplingrate Command
		/// </summary>
		public const byte SAMPLING_INTERVAL = 0x7A;
	}
	
	public enum PinModes
	{
		/// <summary>
		/// Pinmode INPUT
		/// </summary>
		INPUT = 0x00,

		/// <summary>
		/// Pinmode OUTPUT
		/// </summary>
		OUTPUT = 0x01,

		/// <summary>
		/// Pinmode ANALOG (This is not implemented in the standard firmata program)
		/// </summary>
		ANALOG = 0x02,
		
		/// <summary>
		/// Pinmode PWM
		/// </summary>
		PWM = 0x03,
		
		/// <summary>
		/// Pinmode SERVO
		/// </summary>
		SERVO = 0x04,
	}
	
	public static class ATMegaPorts
	{
		/// <summary>
		/// This port represents digital pins 0..7. Pins 0 and 1 are reserved for communication
		/// </summary>
		public const byte PORTD = 0;
		
		/// <summary>
		/// This port represents digital pins 8..13. 14 and 15 are for the crystal
		/// </summary>
		public const byte PORTB = 1;
		
		/// <summary>
		/// This port represents analog input pins 0..5
		/// </summary>
		public const byte PORTC = 2;
		
		public static byte getPortForIndex(int index)
		{
			switch (index)
			{
				case 0: return PORTD;
				case 1: return PORTB;
				case 2: return PORTC;
			}
			return 0;
		}
	}
	
	#endregion
	
	
}
