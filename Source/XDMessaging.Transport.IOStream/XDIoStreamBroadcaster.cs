﻿/*=============================================================================
*
*	(C) Copyright 2013, Michael Carlisle (mike.carlisle@thecodeking.co.uk)
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
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using TheCodeKing.Utils.Contract;
using TheCodeKing.Utils.Serialization;
using XDMessaging.Messages;

namespace XDMessaging.Transport.IOStream
{
    /// <summary>
    /// 	A concrete implementation of IXDBroadcast which can be used to send messages across
    /// 	appDomain and process boundaries using file IO streams to a shared directory. Instances
    /// 	of XDIOStreamListener can be used to receive the messages in another process.
    /// </summary>
    [XDBroadcasterHint(XDTransportMode.Compatibility)]
// ReSharper disable InconsistentNaming
    public sealed class XDIOStreamBroadcaster : IXDBroadcaster
// ReSharper restore InconsistentNaming
    {
        #region Constants and Fields

        /// <summary>
        /// 	The timeout period after which messages are deleted.
        /// </summary>
        private const int fileTimeoutMilliseconds = 5000;

        /// <summary>
        /// 	Unique mutex key to synchronize the clean up tasks across processes.
        /// </summary>
        private const string mutexCleanUpKey = @"Global\XDIOStreamBroadcastv4.Cleanup";

        /// <summary>
        /// 	Get a list of charactors that must be stripped from a channel name folder.
        /// </summary>
        private static readonly char[] invalidChannelChars = Path.GetInvalidFileNameChars();

        /// <summary>
        /// 	The temporary folder where messages will be stored.
        /// </summary>
        private static readonly string temporaryFolder;

        private readonly ISerializer serializer;

        /// <summary>
        /// 	Is this instance capable
        /// </summary>
        public bool IsAlive
        {
            get { return true; }
        }

        #endregion

        #region Constructors and Destructors

        /// <summary>
        /// 	Static constructor gets the path to the temporary directory.
        /// </summary>
        static XDIOStreamBroadcaster()
        {
            temporaryFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                           "XDMessagingv4");
        }

        internal XDIOStreamBroadcaster(ISerializer serializer)
        {
            Validate.That(serializer).IsNotNull();

            this.serializer = serializer;
        }

        #endregion

        #region Implemented Interfaces

        #region IXDBroadcaster

        public void SendToChannel(string channelName, object message)
        {
            Validate.That(channelName).IsNotNullOrEmpty();
            Validate.That(message).IsNotNull();

            SendToChannel(channelName, message.GetType().AssemblyQualifiedName, serializer.Serialize(message));
        }

        /// <summary>
        /// 	The implementation of IXDBroadcast, used to broadcast a new message to other processes. This creates a unique
        /// 	file on the filesystem. The temporary files are cleaned up after a pre-defined timeout.
        /// </summary>
        /// <param name = "channelName"></param>
        /// <param name = "message"></param>
        public void SendToChannel(string channelName, string message)
        {
            SendToChannel(channelName, typeof (string).AssemblyQualifiedName, message);
        }

        #endregion

        #endregion

        #region Methods

        /// <summary>
        /// 	The implementation of IXDBroadcast, used to broadcast a new message to other processes. This creates a unique
        /// 	file on the filesystem. The temporary files are cleaned up after a pre-defined timeout.
        /// </summary>
        /// <param name = "channelName"></param>
        /// <param name = "dataType"></param>
        /// <param name = "message"></param>
        private void SendToChannel(string channelName, string dataType, string message)
        {
            Validate.That(channelName).IsNotNullOrEmpty();
            Validate.That(dataType).IsNotNullOrEmpty();
            Validate.That(message).IsNotNullOrEmpty();

            // create temporary name
            string fileName = Guid.NewGuid().ToString();
            string folder = GetChannelDirectory(channelName);
            string filePath = Path.Combine(folder, string.Concat(fileName, ".msg"));
            // write the message to the temp file, which will trigger listeners in other processes
            using (var writer = File.CreateText(filePath))
            {
                // write out the channel name and message, this allows for invalid
                // characters in the channel name.
                var dataGram = new DataGram(channelName, dataType, message);
                writer.Write(serializer.Serialize(dataGram));
                writer.Flush();
            }
            // return as fast as we can, leaving a clean up task
            ThreadPool.QueueUserWorkItem(CleanUpMessages, new FileInfo(filePath).Directory);
        }

        /// <summary>
        /// 	A helper method used to determine the temporary directory location used for
        /// 	a particular channel. The directory is created if it does not exist.
        /// </summary>
        /// <param name = "channelName"></param>
        /// <returns></returns>
        internal static string GetChannelDirectory(string channelName)
        {
            string folder = null;
            try
            {
                string channelKey = GetChannelKey(channelName);
                folder = Path.Combine(temporaryFolder, channelKey);
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                return folder;
            }
            catch (PathTooLongException e)
            {
                throw new ArgumentException(string.Format("Unable to bind to channel as the name '{0}' is too long." +
                                                          " Try a shorter channel name.", channelName), e);
            }
            catch (UnauthorizedAccessException ue)
            {
                throw new UnauthorizedAccessException(
                    string.Format("Unable to bind to channel '{0}' as access is denied." +
                                  " Ensure the process has read/write access to the directory '{1}'.", channelName,
                                  folder), ue);
            }
            catch (IOException ie)
            {
                throw new IOException(string.Format("There was an unexpected IO error binding to channel '{0}'." +
                                                    " Ensure the process is unable to read/write to directory '{1}'.",
                                                    channelName, folder), ie);
            }
        }

        /// <summary>
        /// 	Gets a channel key string associated with the channel name. This is used as the 
        /// 	directory name in the temporary directory, and we therefore strip out any invalid characters.
        /// </summary>
        /// <param name = "channelName">The channel name for which a channel key is required.</param>
        /// <returns>The string channel key.</returns>
        internal static string GetChannelKey(string channelName)
        {
            foreach (var c in invalidChannelChars)
            {
                if (channelName.Contains(c.ToString()))
                {
                    channelName = channelName.Replace(c, '_');
                }
            }
            return channelName;
        }

        /// <summary>
        /// 	This method is called within a seperate thread and deletes messages that are older than
        /// 	the pre-defined expiry time.
        /// </summary>
        /// <param name = "state"></param>
        private static void CleanUpMessages(object state)
        {
            var directory = (DirectoryInfo) state;

            // use a mutex to ensure only one listener system wide is running
            bool createdNew;
            string mutexName = string.Concat(mutexCleanUpKey, ".", directory.Name);
            var accessControl = new MutexSecurity();
            var sid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            accessControl.SetAccessRule(new MutexAccessRule(sid, MutexRights.FullControl, AccessControlType.Allow));
            using (var mutex = new Mutex(true, mutexName, out createdNew, accessControl))
            {
                // we this thread owns the Mutex then clean up otherwise exit.
                if (createdNew)
                {
                    // wait for the specified timeout before attempting to clean directory
                    try
                    {
                        Thread.Sleep(fileTimeoutMilliseconds);
                    }
                    catch (ThreadInterruptedException)
                    {
                    }
                    CleanUpMessages(directory);
                    // release the mutex
                    mutex.ReleaseMutex();
                }
            }
            if (createdNew)
            {
                // after mutex release add an additional thread for cleanup in case we're the last out
                // and there are now additional files to clean
                ThreadPool.QueueUserWorkItem(CleanUpMessages, directory);
            }
        }

        /// <summary>
        /// 	Helper method to delete messages form the given directory older
        /// 	than the specified timeout.
        /// </summary>
        /// <param name = "directory"></param>
        private static void CleanUpMessages(DirectoryInfo directory)
        {
            try
            {
                // check directory not deleted, don't use cached version (directory.Exists)
                if (Directory.Exists(directory.FullName))
                {
                    foreach (var file in directory.GetFiles("*.msg"))
                    {
                        // attempt to clean up all expired messages in the channel directory
                        if (file.CreationTimeUtc <= DateTime.UtcNow.AddMilliseconds(-fileTimeoutMilliseconds))
                        {
                            if (File.Exists(file.FullName))
                            {
                                try
                                {
                                    file.Delete();
                                }
                                catch (IOException)
                                {
                                } // the file could have been deleted by another broadcaster, retry later.
                                catch (UnauthorizedAccessException)
                                {
                                } // if the file is still in use retry again later.
                            }
                        }
                    }
                }
            }
            catch (IOException)
            {
            } // the file could have been deleted by another broadcaster, retry later.
            catch (UnauthorizedAccessException)
            {
            } // if the file is still in use retry again later.
        }

        #endregion
    }
}