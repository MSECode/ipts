/*
 * Copyright (C) 2025 Istituto Italiano di Tecnologia
 * Authors: davide.tome@iit.it, jacopo.losi@iit.it
 * CopyPolicy: Released under the terms of the LGPLv2.1 or later, see LGPL.TXT
 */

using Esd.IO.Ntcan;
using log4net;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace iCubProductionTestSuite.classes
{

    public class CanUtils
    {
        private List<String> ports;
        private CanPort port;
        private int netPort = -1;
        private int messageId;
        private bool messageFilterConfigured = false;

        private List<string> lastSentData = null;
        private int maxSendRetries = 2;
        private int maxReceiveRetries = 2;
        private int receiveTimeoutMs = 5000;
        private int transmitTimeoutMs = 1000;

        private static readonly ILog log = LogManager.GetLogger(typeof(CanUtils));

        public CanUtils() { }

        public CanUtils(TestInterface ti)
        {
            Configure(ti);
        }

        // Get list of available CAN ports
        private List<String> getPorts() 
        {
            ports = new List<string>();

            foreach(CanPortInfo  p in CanPortInfo.Ports)
            {
                ports.Add(p.NetNo.ToString());
            }
            return ports;

        }

      
        public List<String> Ports
        {
            get
            {
                ports = getPorts();
                return ports;
            }

        }

        public CanPort Port
        {
            get
            {
                return port;
            }

            set
            {
                port = value;
                messageFilterConfigured = false;
            }
        }

        public void Configure(TestInterface ti)
        {
            int configuredNetPort = Convert.ToInt16(ti.NetPort);
            int configuredMessageId = Convert.ToInt32(ti.MessageID, 16);

            if (port != null && netPort == configuredNetPort && messageId == configuredMessageId)
            {
                return;
            }

            ClosePort();
            if (port != null)
            {
                port.Dispose();
            }

            netPort = configuredNetPort;
            messageId = configuredMessageId;
            port = new CanPort(
                netPort,
                CanPortMode.FifoMode,
                receiveTimeoutMs,
                transmitTimeoutMs,
                128,
                32);
            messageFilterConfigured = false;
        }

        private void EnsurePortOpen()
        {
            if (port == null)
            {
                throw new InvalidOperationException("CAN port is not configured");
            }

            if (!port.IsOpen)
            {
                port.Open();
                port.BitRate = new CanBitRate(CanBitRateTable.Cia1000KBit);
                port.ReceiveTimeout = receiveTimeoutMs;
                port.TransmitTimeout = transmitTimeoutMs;
                messageFilterConfigured = false;
            }

            if (!messageFilterConfigured)
            {
                port.AddToMessageFilter(CanMessageType.Data, messageId);
                messageFilterConfigured = true;
            }
        }

        private void ClosePort()
        {
            if (port != null && port.IsOpen)
            {
                port.Close();
            }
            messageFilterConfigured = false;
        }

       
        public bool send(List<string> data)
        {
            lastSentData = new List<string>(data);
            int attempts = 0;
            bool sent = false;

            while (attempts < maxSendRetries && !sent)
            {
                //TODO: review try-catch block and decouple port open/close from send to trigger correctly the exceptions
                try
                {
                    EnsurePortOpen();
                    port.PurgeReceiveBuffer();

                    CanMessage cmsg = new CanMessage
                    {
                        Identifier = 0x001,
                        DataLength = Convert.ToByte(data.Count)
                    };
                    for (int i = 0; i < data.Count; i++)
                        cmsg[i] = Convert.ToByte(data[i]);

                    port.Write(ref cmsg);
                    sent = true;
                    log.InfoFormat("Sent CAN message: {0}", cmsg.ToString());
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show("Errore apertura CAN port: " + ex.Message, "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false; // No point retrying if the port can't be opened due to invalid state
                }
                catch (IOException)
                {
                    ClosePort();
                    attempts++;
                    if (attempts >= maxSendRetries)
                    {
                        MessageBox.Show("Errore invio CAN dopo vari tentativi!", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return false;
                    }
                    System.Threading.Thread.Sleep(100); // Small delay before retry
                }
            }
            return sent;
        }

        public bool TryReceive(List<string> prev_data, out CanMessage cmsg)
        {
            bool received = false;
            lastSentData = prev_data;
            cmsg = new CanMessage();

            if (lastSentData == null)
            {
                MessageBox.Show("Nessun messaggio CAN inviato da ritentare!", "Errore", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            int attempts = 0;

            while (attempts < maxReceiveRetries && !received)
            {
                try
                {
                    EnsurePortOpen();

                    // Try to read a CAN message
                    int readMessages = port.Read(ref cmsg);
                    if (readMessages == 1 && cmsg.DataLength > 0)
                    {
                        received = true;
                        log.Debug(cmsg.ToString());
                        lastSentData.Clear();
                        return true;
                    }

                    log.DebugFormat("No valid CAN message received. Read returned {0}, data length is {1}", readMessages, cmsg.DataLength);
                }
                catch (IOException)
                {
                    // Optionally handle port errors here
                    ClosePort();
                    MessageBox.Show("Problemi nella ricezione dal CAN port. Ritento...", "Warning CAN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                // If not received, resend the last message and try again
                log.Debug("Message not yet received. Retrying...");
                send(lastSentData);
                attempts++;
            }

            MessageBox.Show("CAN timeout dopo vari tentativi di ricezione!", "Errore CAN", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        public CanMessage receive(List<string> prev_data)
        {
            CanMessage cmsg;
            if (TryReceive(prev_data, out cmsg))
            {
                return cmsg;
            }

            return default(CanMessage);
        }
    }
}
