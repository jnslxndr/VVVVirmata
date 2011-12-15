#region Copyright notice
/*
A Firmata Plugin for VVVV
----------------------------------
Encoding control and configuration messages for Firmata enabled MCUs. This
Plugin encodes to a ANSI string and a byte array, so you can send via any
interface, most likely RS-232 a.k.a. Comport to a - most likely - Arduino.

For more information on Firmata see: http://firmata.org
Get the source here: https://github.com/jens-a-e/VirmataEncoder
Any issues should be posted here: https://github.com/jens-a-e/VirmataEncoder/issues

Copyleft 2011
Jens Alexander Ewald, http://ififelse.net
Chris Engler, http://wirmachenbunt.de

Inspired by the Sharpduino project by Tasos Valsamidis (LSB and MSB operations)
See http://code.google.com/p/sharpduino if interested.


Copyright notice
----------------

This is free and unencumbered software released into the public domain.

Anyone is free to copy, modify, publish, use, compile, sell, or
distribute this software, either in source code form or as a compiled
binary, for any purpose, commercial or non-commercial, and by any
means.

In jurisdictions that recognize copyright laws, the author or authors
of this software dedicate any and all copyright interest in the
software to the public domain. We make this dedication for the benefit
of the public at large and to the detriment of our heirs and
successors. We intend this dedication to be an overt act of
relinquishment in perpetuity of all present and future rights to this
software under copyright law.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR
OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
OTHER DEALINGS IN THE SOFTWARE.

For more information, please refer to <http://unlicense.org/>
*/
#endregion

#region usings
using System;
using System.ComponentModel.Composition;
using System.Collections.Generic;
using System.Text;

using VVVV.PluginInterfaces.V1;
using VVVV.PluginInterfaces.V2;
using VVVV.Core.Logging;

using Firmata;
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
		IDiffSpread<PinMode> PinModeSetup;
		
		[Input("ReportAnalogPins",IsSingle = true, DefaultValue = 1)]
		IDiffSpread<bool> ReportAnalogPins;
		
		[Input("ReportDigitalPins",IsSingle = true, DefaultValue = 1)]
		IDiffSpread<bool> ReportDigitalPins;
		
		[Input("Reset",IsSingle = true, IsBang=true, DefaultValue = 0)]
		IDiffSpread<bool> ResetSystem;
		
		//// Use a default SamplingRate of 40ms
		[Input("Samplerate", MinValue = 0, DefaultValue = 40,IsSingle = true, Visibility = PinVisibility.Hidden)]
		IDiffSpread<int> Samplerate;
		
		[Input("SendOnCreate", Visibility = PinVisibility.Hidden, IsSingle = true, DefaultValue = 1)]
		IDiffSpread<bool> SendOnCreate;
		
		[Input("ReportFirmwareVersion",IsSingle = true, Visibility = PinVisibility.OnlyInspector, IsBang=true)]
		IDiffSpread<bool> ReportFirmwareVersion;
		
		[Input("AnalogInputCount",DefaultValue = 6, Visibility = PinVisibility.OnlyInspector, IsSingle = true)]
		ISpread<int> analogInputCount;
		
		[Input("DigitalInputCount",DefaultValue = 14, Visibility = PinVisibility.OnlyInspector, IsSingle = true)]
		ISpread<int> digitalInputCount;
		
		///
		/// OUTPUT
		///
		[Output("Firmatamessage")]
		ISpread<string> FirmataOut;
		
		[Output("Change")]
		ISpread<bool> ChangedOut;
		
		[Output("RAW", Visibility = PinVisibility.Hidden)]
		ISpread<byte[]> RawOut;
		
		
		[Import()]
		ILogger FLogger;
		
		/// Use a Queue for a command byte buffer:
		Queue<byte> CommandBuffer = new Queue<byte>(1024);
		
		/// EVALUATE
		public void Evaluate(int SpreadMax)
		{
			// Clear the buffer before everey run
			CommandBuffer.Clear();
			
			if(PinModeSetup.IsChanged || ShouldReset || !PINS_CONFIGURED)
			UpdatePinConfiguration();
			
			
			
			if (PinModeSetup.IsChanged || PinValues.IsChanged || ShouldReset)
			SetPinStates(PinValues);
			
			/// Set Pinreporting for analog pins
			// TODO: It should not be a fixed number of pins, later versions
			// TODO: if spread has only one value, do all, otherwise do given, there are 16!
			if (ReportAnalogPins.IsChanged || ShouldReset)
			SetAnalogPinReportingForRange(analogInputCount[0],ReportAnalogPins[0]);
			
			/// Set Pinreporting for digital pins
			if (ReportDigitalPins.IsChanged || ShouldReset)
			{
				// TODO: Check which pin number should be reported and enable only the proper port.
				// TODO: It could work like: if spread.slicecount==1 do all, else do specific pins
				GetDigitalPinReportingCommandForState(ReportDigitalPins[0],Port.PORTB);
				GetDigitalPinReportingCommandForState(ReportDigitalPins[0],Port.PORTD);
			}
			
			if(Samplerate.IsChanged || ShouldReset)
			{
				// We must shortly trun of the reporting to get immidiate change of rate
				if (ReportAnalogPins[0]) SetAnalogPinReportingForRange(analogInputCount[0],false);
				GetSamplerateCommand(Samplerate[0]);
				if (ReportAnalogPins[0]) SetAnalogPinReportingForRange(analogInputCount[0],true);
			}
			
			if (ReportFirmwareVersion.IsChanged) GetFirmwareVersionCommand();
			
			if(ResetSystem.IsChanged && ShouldReset)
			GetResetCommand();
			
			ChangedOut[0] = CommandBuffer.Count>0;
			RawOut[0]     = CommandBuffer.ToArray();
			FirmataOut[0] = Encoder.GetString(RawOut[0]);
		}
		
		
		#region Helper Functions
		
		/// Use ANSI Encoding for the Encoder
		static Encoding Encoder = Encoding.GetEncoding(1252);
		
		byte[] OUTPUT_PORT_MASKS  = {}; // empty array
		
		PinMode DEFAULT_PINMODE = PinMode.OUTPUT;
		
		double VALUE_THRESHOLD = 0.5;
		
		int NUM_OUTPUT_PORTS = 0;
		int NUM_PORTS = 0; // The total number of ports (AVR PORTS) respective to the number of pins
		int NUM_PINS  = 0; // The total number of pins addressed by this node
		
		bool PINS_CONFIGURED = false;
		bool PIN_CONFIG_CHANGED = false;
		
		// Make a shortcut for ResetSystem[0]
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
		
		
		PinMode PinModeForPin(int pin)
		{
			return pin<PinModeSetup.SliceCount ? PinModeSetup[pin]:DEFAULT_PINMODE;
		}
		
		/// <summary>
		/// Updates the pin masks, number of pins ,etc
		/// </summary>
		void UpdatePinConfiguration()
		{
			PIN_CONFIG_CHANGED = false;
			
			UpdatePinCount();
			
			/// TODO Optimize to use actual needed output ports, instead of all
			NUM_OUTPUT_PORTS = NUM_PORTS;
			OUTPUT_PORT_MASKS = new byte[NUM_OUTPUT_PORTS];
			
			// allocate memory once
			byte output_port;
			for(int i = 0; i<NUM_PORTS; i++)
			{
				// reset temporary port mask
				output_port=0x00;
				
				// Build the mask
				for (int bit=0; bit<8; bit++)
				{
					int src_index = i*8+bit;
					PinMode mode = DEFAULT_PINMODE;
					
					/// Set the mode and add to the configure command
					if(src_index<PinModeSetup.SliceCount)
					{
						mode = PinModeSetup[src_index];
						CommandBuffer.Enqueue(Command.SETPINMODE);
						CommandBuffer.Enqueue((byte) src_index);
						CommandBuffer.Enqueue((byte) mode);
					}
					
					output_port |= (byte)((mode == PinMode.OUTPUT ? 1:0)<<bit);
				}
				OUTPUT_PORT_MASKS[i] = output_port;
			}
			
			// Pin have been configured once:
			PINS_CONFIGURED = true;
			
			// Signal change
			PIN_CONFIG_CHANGED = true;
		}
		
		
		
		void SetPinModeCommand(PinMode mode, int pin)
		{
			CommandBuffer.Enqueue(Command.SETPINMODE);
			CommandBuffer.Enqueue((byte) pin);
			CommandBuffer.Enqueue((byte) mode);
		}
		
		static int PortIndexForPin(int pin)
		{
			return pin/8;
		}
		
		void SetPinStates(ISpread<double> values)
		{
			// get the number of output ports
			// FIXME: Make MAX_PORTS avaiable through Firmata
			int[] digital_out = new int[OUTPUT_PORT_MASKS.Length];
			
			for(int i=0; i<values.SliceCount; i++)
			{
				double value = values[i];
				PinMode mode = PinModeForPin(i);
				switch(mode)
				{
					case PinMode.ANALOG:
					case PinMode.PWM:
					case PinMode.SERVO:
					byte LSB,MSB;
					value *= mode==PinMode.SERVO ? 180 : 255; // servo is in degrees
					FirmataUtils.GetBytesFromValue((int)value,out MSB,out LSB);
					CommandBuffer.Enqueue((byte)(Command.ANALOGMESSAGE | i));
					CommandBuffer.Enqueue(LSB);
					CommandBuffer.Enqueue(MSB);
					break;
					
					case PinMode.OUTPUT:
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
				byte atmega_port = (byte) port_index; //Array.GetValues(Port)[port_index];
				FirmataUtils.GetBytesFromValue(digital_out[port_index],out MSB,out LSB);
				
				CommandBuffer.Enqueue((byte)(Command.DIGITALMESSAGE | atmega_port));
				CommandBuffer.Enqueue(LSB);
				CommandBuffer.Enqueue(MSB);
				
			}
		}
		
		
		void GetSamplerateCommand(int rate)
		{
			byte lsb,msb;
			FirmataUtils.GetBytesFromValue(rate,out msb,out lsb);
			CommandBuffer.Enqueue(Command.SYSEX_START);
			CommandBuffer.Enqueue(Command.SAMPLING_INTERVAL);
			CommandBuffer.Enqueue(lsb);
			CommandBuffer.Enqueue(msb);
			CommandBuffer.Enqueue(Command.SYSEX_END);
		}
		
		
		/// Query Firmware Name and Version
		void GetFirmwareVersionCommand()
		{
			CommandBuffer.Enqueue(Command.SYSEX_START);
			CommandBuffer.Enqueue(Command.REPORT_FIRMWARE_VERSION);
			CommandBuffer.Enqueue(Command.SYSEX_END);
		}
		
		void GetAnalogPinReportingCommandForState(bool state,int pin)
		{
			byte val = (byte) (state ? 0x01 : 0x00);
			CommandBuffer.Enqueue((byte)(Command.TOGGLEANALOGREPORT|pin));
			CommandBuffer.Enqueue(val);
		}
		
		void SetAnalogPinReportingForRange(int range, bool state)
		{
			for(int i = 0; i<range; i++)
			GetAnalogPinReportingCommandForState(state,i);
		}
		
		void GetDigitalPinReportingCommandForState(bool state,Port port)
		{
			byte val = (byte) (state ? 0x01 : 0x00);
			CommandBuffer.Enqueue((byte)(Command.TOGGLEDIGITALREPORT|(int)port));
			CommandBuffer.Enqueue(val);
		}
		
		void GetResetCommand()
		{
			CommandBuffer.Enqueue(Command.SYSEX_START);
			CommandBuffer.Enqueue(Command.RESET);
			CommandBuffer.Enqueue(Command.SYSEX_END);
		}
		
		#endregion
		
	}
	
	
}
