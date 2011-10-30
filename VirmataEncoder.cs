


#region usings
using System;
using System.ComponentModel.Composition;
using System.Collections.Generic;
using System.Text;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Core.Logging;
#endregion usings

namespace VVVV.Nodes
{
	#region PluginInfo
	[PluginInfo(Name = "FirmataEncode",
	Category = "Devices",
	Version = "Firmata v2.2",
	Help = "Encodes pins, values and commands for Firmata (Protocol v2.2)",
	Tags = "Devices,Encoders")]
	#endregion PluginInfo
	
	public class FirmataEncode : IPluginEvaluate
	{
		///
		/// INPUT
		///
		[Input("Values")]
		IDiffSpread<double> PinValues;
		
		[Input("PinModes", DefaultEnumEntry = "INPUT")]
		IDiffSpread<PinModes> PinModeSetup;
		
		[Input("report analog pins",IsSingle = true, DefaultValue = 1)]
		IDiffSpread<bool> ReportAnalogPins;
		
		[Input("ReportDigitalPins",IsSingle = true, DefaultValue = 1)]
		IDiffSpread<bool> ReportDigitalPins;
		
		//// Use a default SamplingRate of 40ms
		[Input("Samplerate", MinValue = 0, DefaultValue = 40,IsSingle = true)]
		IDiffSpread<int> Samplerate;
		
		[Input("ReportFirmwareVersion",IsSingle = true, Visibility = PinVisibility.OnlyInspector, IsBang=true)]
		IDiffSpread<bool> ReportFirmwareVersion;
		
		[Input("Reset",IsSingle = true, Visibility = PinVisibility.Hidden, IsBang=true, DefaultValue = 0)]
		IDiffSpread<bool> ResetSystem;
		
		[Input("SendOnCreate", Visibility = PinVisibility.Hidden, IsSingle = true, DefaultValue = 1)]
		IDiffSpread<bool> SendOnCreate;
		
		///
		/// OUTPUT
		///
		[Output("Firmatamessage")]
		ISpread<string> FirmataOut;
		
		[Output("RAW")]
		ISpread<byte[]> RawOut;
		
		[Output("Change")]
		ISpread<bool> ChangedOut;
		
		[Import()]
		ILogger FLogger;
		
		/// EVALUATE
		public void Evaluate(int SpreadMax)
		{
			string command_out = "";
			
//			if(ResetSystem.IsChanged && ShouldReset) command_out += GetResetCommand();
			
			if( PinModeSetup.IsChanged || ShouldReset || !PINS_CONFIGURED)
				command_out += UpdatePinConfiguration();
			
			if (ReportFirmwareVersion.IsChanged) command_out += GetFirmwareVersionCommand();
			
			if (PinValues.IsChanged || ShouldReset) command_out += SetPinStates(PinValues);
			
			
			/// Set Pinreporting for analog pins
			// TODO: It should not be a fixed number of pins, later versions
			// TODO: if spread has only one value, do all, otherwise do given, there are 16!
			if (ReportAnalogPins.IsChanged || ShouldReset)
			command_out += SetAnalogPinReportingForRange(6,ReportAnalogPins[0]);
			
			/// Set Pinreporting for digital pins
			if (ReportDigitalPins.IsChanged || ShouldReset)
			{
				// TODO: Check which pin number should be reported and enable only the proper port.
				// TODO: It could work like: fi spread.slicecount==1 do all, else do specific pins
				command_out += GetDigitalPinReportingCommandForState(ReportDigitalPins[0],ATMegaPorts.PORTB);
				command_out += GetDigitalPinReportingCommandForState(ReportDigitalPins[0],ATMegaPorts.PORTC);
				command_out += GetDigitalPinReportingCommandForState(ReportDigitalPins[0],ATMegaPorts.PORTD);
			}
			
			if(Samplerate.IsChanged || ShouldReset)
			{
				// We must shortly trun of the reporting to get immidiate change of rate
				if (ReportAnalogPins[0])
				command_out += SetAnalogPinReportingForRange(6,false);
				
				command_out += GetSamplerateCommand(Samplerate[0]);
				
				if (ReportAnalogPins[0])
				command_out += SetAnalogPinReportingForRange(6,ReportAnalogPins[0]);
			}
			
			bool something_changed = (command_out!="");
			ChangedOut[0] = something_changed;
			FirmataOut[0] = command_out;
		}
		
		
		#region Helper functions
		
		byte[] OUTPUT_PORT_MASKS  = {}; // empty array
		
		/// NOT USED:
		Queue<byte> INPUT_PORT_MASKS  = new Queue<byte>();
		Queue<byte> ANALOG_PORT_MASKS = new Queue<byte>();
		Queue<byte> PWM_PORT_MASKS    = new Queue<byte>();
		
		PinModes DEFAULT_PINMODE = PinModes.OUTPUT;
		
		double VALUE_THRESHOLD = 0.5;
		
		int NUM_OUTPUT_PORTS = 0;
		int NUM_PORTS = 0; // The total number of ports (AVR PORTS) respective to the number of pins
		int NUM_PINS  = 0; // The total number of pins addressed by this node
		
		bool PINS_CONFIGURED = false;
		bool PIN_CONFIG_CHANGED = false;
		
		bool ShouldReset { get {return ResetSystem[0];} set{} }
		
		/// <summary>
		/// Calculate the total number of pins addressed with this node
		/// </summary>
		void UpdatePinCount()
		{
			/// Who wins?
			NUM_PINS  = PinModeSetup.SliceCount >= PinValues.SliceCount ?  PinModeSetup.SliceCount : PinValues.SliceCount;
			/// calculate the next full divider by 8:
			NUM_PORTS = NUM_PINS/8 + (NUM_PINS%8==0 ? 0 : 1);
		}
		
		
		PinModes PinModeForPin(int pin)
		{
			return pin<PinModeSetup.SliceCount ? PinModeSetup[pin]:DEFAULT_PINMODE;
		}
		
		/// <summary>
		/// Updates the pin masks, number of pins ,etc
		/// </summary>
		string UpdatePinConfiguration()
		{
			PIN_CONFIG_CHANGED = false;
			
			// Do not do it if nothing changed, but do it if the pins have not yet been configured
			//			if (PINS_CONFIGURED) return "";
			
			UpdatePinCount();
			
			/// TODO Optimize to use actual needed output ports, instead of all
			NUM_OUTPUT_PORTS = NUM_PORTS;
			OUTPUT_PORT_MASKS = new byte[NUM_OUTPUT_PORTS];
			
			// build the coammnd for pin configuration in parallel
			Queue<byte> config_command = new Queue<byte>();
			
			// allocate memory once
			byte input_port,output_port,analog_port,pwm_port;
			for(int i = 0; i<NUM_PORTS; i++)
			{
				// reset temporary port mask
				input_port=output_port=analog_port=pwm_port=0x00;
				
				// Build the mask
				for (int bit=0; bit<8; bit++)
				{
					int src_index = i*8+bit;
					PinModes mode = DEFAULT_PINMODE;
					
					/// Set the mode and add to the configure command
					if(src_index<PinModeSetup.SliceCount)
					{
						mode = PinModeSetup[src_index];
						config_command.Enqueue(FirmataCommands.SETPINMODE);
						config_command.Enqueue((byte) src_index);
						config_command.Enqueue((byte) mode);
					}
					
					input_port  |= (byte)((mode == PinModes.INPUT  ? 1:0)<<bit);
					output_port |= (byte)((mode == PinModes.OUTPUT ? 1:0)<<bit);
					analog_port |= (byte)((mode == PinModes.ANALOG ? 1:0)<<bit);
					pwm_port    |= (byte)((mode == PinModes.PWM    ? 1:0)<<bit);
					
					/// TODO: If we have a servo, we should send a default config to the command queue
				}
				OUTPUT_PORT_MASKS[i] = output_port;
				
				/// These are not needed actually...
				INPUT_PORT_MASKS.Enqueue(input_port);
				ANALOG_PORT_MASKS.Enqueue(analog_port);
				PWM_PORT_MASKS.Enqueue(pwm_port);
				
			}
			
			
			// Pin have been configured once:
			PINS_CONFIGURED = true;
			
			// Signal change
			PIN_CONFIG_CHANGED = true;
			
			return Encoder.GetString(config_command.ToArray());
		}
		
		/// Use ANSI Encoding!
		static Encoding Encoder = Encoding.GetEncoding(1252);
		
		/* This is a shortcut to encode byte arrays, which also contain bytes higer than 127 */
		static string Encode(byte[] bytes) {
			return Encoder.GetString(bytes);
		}
		
		static string SetPinModeCommand(PinModes mode, int pin)
		{
			byte[] cmd = {
				FirmataCommands.SETPINMODE,
				(byte) pin,
				(byte) mode
			};
			return Encoder.GetString(cmd);
		}
		
		static int PortIndexForPin(int pin)
		{
			return pin/8;
		}
		
		string SetPinStates(ISpread<double> values)
		{
			// TODO: handle PWM set pins!
			// Maybe this should return an object like Ports.ANALOG, PORTS.DIGITAL
			
			Queue<byte> cmd = new Queue<byte>();
			
			// get the number of output ports
			int[] digital_out = new int[OUTPUT_PORT_MASKS.Length];
			
			for(int i=0; i<values.SliceCount; i++)
			{
				double value = values[i];
				PinModes mode = PinModeForPin(i);
				switch(mode)
				{
					case PinModes.ANALOG:
					case PinModes.PWM:
					case PinModes.SERVO:
					byte LSB,MSB;
					value *= mode==PinModes.SERVO ? 180 : 255; // servo is in degrees
					GetBytesFromValue((int)value,out MSB,out LSB);
					cmd.Enqueue((byte)(FirmataCommands.ANALOGMESSAGE | i));
					cmd.Enqueue(LSB);
					cmd.Enqueue(MSB);
					break;
					
					case PinModes.OUTPUT:
					int port_index = PortIndexForPin(i);
					// Break, if we have no ouputports we can get
					if (port_index >= digital_out.Length) break;
					
					int shift = i%8;
					int state = value >= 0.5 ? 0x01 : 0x00;
					int port_before = digital_out[port_index];
					digital_out[port_index] =  ((state << shift) | digital_out[port_index])  & OUTPUT_PORT_MASKS[port_index];
					break;
				}
			}
			
			/// Write all the output ports to the command buffer
			for(int port_index=0; port_index<digital_out.Length; port_index++)
			{
				byte LSB,MSB;
				byte atmega_port = ATMegaPorts.getPortForIndex(port_index);
				GetBytesFromValue(digital_out[port_index],out MSB,out LSB);
				
				cmd.Enqueue((byte)(FirmataCommands.DIGITALMESSAGE | atmega_port));
				cmd.Enqueue(LSB);
				cmd.Enqueue(MSB);
				
			}
			
			return Encoder.GetString(cmd.ToArray());
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
