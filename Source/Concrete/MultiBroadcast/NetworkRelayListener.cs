﻿/*=============================================================================
*
*	(C) Copyright 2007, Michael Carlisle (mike.carlisle@thecodeking.co.uk)
*
*   http://www.TheCodeKing.co.uk
*  
*	All rights reserved.
*	The code and information is provided "as-is" without waranty of any kind,
*	either expressed or implied.
*
*=============================================================================
*/
using System;

namespace TheCodeKing.Net.Messaging.Concrete.MultiBroadcast
{
    /// <summary>
    ///   The implementation used to listen for and relay network messages for all
    ///   instances of IXDListener.
    /// </summary>
    internal sealed class NetworkRelayListener : IDisposable
    {
        #region Constants and Fields

        /// <summary>
        ///   The instance used to broadcast network messages on the local machine.
        /// </summary>
        private readonly IXDBroadcast nativeBroadcast;

        /// <summary>
        ///   The instance of MailSlot used to receive network messages from other machines.
        /// </summary>
        private IXDListener propagateListener;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        ///   Default constructor.
        /// </summary>
        /// <param name = "nativeBroadcast"></param>
        /// <param name = "propagateListener"></param>
        internal NetworkRelayListener(IXDBroadcast nativeBroadcast, IXDListener propagateListener)
        {
            if (nativeBroadcast == null)
            {
                throw new ArgumentNullException("nativeBroadcast");
            }
            if (propagateListener == null)
            {
                throw new ArgumentNullException("propagateListener");
            }
            this.nativeBroadcast = nativeBroadcast;
            this.propagateListener = propagateListener;
            // listen on the network channel for this mode
            this.propagateListener.RegisterChannel(NetworkRelayBroadcast.GetPropagateNetworkMailSlotName(nativeBroadcast));
            this.propagateListener.MessageReceived += OnMessageReceived;
        }

        #endregion

        #region Implemented Interfaces

        #region IDisposable

        /// <summary>
        ///   Implementation of IDisposable used to clean up the listener instance.
        /// </summary>
        public void Dispose()
        {
            if (propagateListener != null)
            {
                propagateListener.Dispose();
                propagateListener = null;
            }
        }

        #endregion

        #endregion

        #region Methods

        /// <summary>
        ///   Handles messages received from other machines on the network and dispatches them locally.
        /// </summary>
        /// <param name = "sender"></param>
        /// <param name = "e"></param>
        private void OnMessageReceived(object sender, XDMessageEventArgs e)
        {
            // network message is of format machine:channel:message
            if (e.DataGram.IsValid)
            {
                NetworkRelayDataGram machineInfo = NetworkRelayDataGram.ExpandFromRaw(e.DataGram.Message);
                if (machineInfo.IsValid)
                {
                    // don't relay if the message was broadcast on this machine
                    if (machineInfo.Channel != Environment.MachineName)
                    {
                        DataGram dataGram = DataGram.ExpandFromRaw(machineInfo.Message);
                        if (dataGram.IsValid)
                        {
                            // propagate the message on this machine using the same mode as the sender
                            nativeBroadcast.SendToChannel(dataGram.Channel, dataGram.Message);
                        }
                    }
                }
            }
        }

        #endregion
    }
}