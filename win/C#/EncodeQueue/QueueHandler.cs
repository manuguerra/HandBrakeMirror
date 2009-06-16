﻿/*  QueueHandler.cs $
 	
 	   This file is part of the HandBrake source code.
 	   Homepage: <http://handbrake.fr/>.
 	   It may be used under the terms of the GNU General Public License. */

using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Xml.Serialization;
using System.Threading;

namespace Handbrake.EncodeQueue
{
    public class QueueHandler
    {
        public Encode encodeHandler = new Encode();
        private static XmlSerializer ser = new XmlSerializer(typeof(List<Job>));
        List<Job> queue = new List<Job>();
        int id; // Unique identifer number for each job

        #region Queue Handling
        public List<Job> getQueue()
        {
            return queue;
        }

        /// <summary>
        /// Get's the next CLI query for encoding
        /// </summary>
        /// <returns>String</returns>
        private Job getNextJobForEncoding()
        {
            Job job = queue[0];
            lastQueueItem = job;
            remove(0);    // Remove the item which we are about to pass out.

            updateQueueRecoveryFile("hb_queue_recovery.xml");

            return job;
        }

        /// <summary>
        /// Get the last query that was returned by getNextItemForEncoding()
        /// </summary>
        /// <returns></returns>
        public Job lastQueueItem { get; set; }

        /// <summary>
        /// Add's a new item to the queue
        /// </summary>
        /// <param name="query">String</param>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        public void add(string query, string source, string destination)
        {
            Job newJob = new Job { Id = id, Query = query, Source = source, Destination = destination };
            id++;

            queue.Add(newJob);
            updateQueueRecoveryFile("hb_queue_recovery.xml");
        }

        /// <summary>
        /// Removes an item from the queue.
        /// </summary>
        /// <param name="index">Index</param>
        /// <returns>Bolean true if successful</returns>
        public void remove(int index)
        {
            queue.RemoveAt(index);
            updateQueueRecoveryFile("hb_queue_recovery.xml");
        }

        /// <summary>
        /// Returns how many items are in the queue
        /// </summary>
        /// <returns>Int</returns>
        public int count()
        {
            return queue.Count;
        }

        /// <summary>
        /// Move an item with an index x, up in the queue
        /// </summary>
        /// <param name="index">Int</param>
        public void moveUp(int index)
        {
            if (index > 0)
            {
                Job item = queue[index];

                queue.RemoveAt(index);
                queue.Insert((index - 1), item);
            }
            updateQueueRecoveryFile("hb_queue_recovery.xml"); // Update the queue recovery file
        }

        /// <summary>
        /// Move an item with an index x, down in the queue
        /// </summary>
        /// <param name="index">Int</param>
        public void moveDown(int index)
        {
            if (index < queue.Count - 1)
            {
                Job item = queue[index];

                queue.RemoveAt(index);
                queue.Insert((index + 1), item);
            }
            updateQueueRecoveryFile("hb_queue_recovery.xml"); // Update the queue recovery file
        }

        /// <summary>
        /// Writes the current queue to disk. hb_queue_recovery.xml
        /// This function is called after getNextItemForEncoding()
        /// </summary>
        public void updateQueueRecoveryFile(string file)
        {
            string tempPath = file == "hb_queue_recovery.xml" ? Path.Combine(Path.GetTempPath(), "hb_queue_recovery.xml") : file;

            try
            {
                using (FileStream strm = new FileStream(tempPath, FileMode.Create, FileAccess.Write))
                {
                    ser.Serialize(strm, queue);
                    strm.Close();
                    strm.Dispose();
                }
            }
            catch (Exception)
            {
                // Any Errors will be out of diskspace/permissions problems. 
                // Don't report them as they'll annoy the user.
            }
        }

        /// <summary>
        /// Writes the current queue to disk to the location specified in file
        /// </summary>
        /// <param name="file"></param>
        public void writeBatchScript(string file)
        {
            string queries = "";
            foreach (Job queue_item in queue)
            {
                string q_item = queue_item.Query;
                string fullQuery = '"' + Application.StartupPath + "\\HandBrakeCLI.exe" + '"' + q_item;

                if (queries == string.Empty)
                    queries = queries + fullQuery;
                else
                    queries = queries + " && " + fullQuery;
            }
            string strCmdLine = queries;

            if (file != "")
            {
                try
                {
                    // Create a StreamWriter and open the file, Write the batch file query to the file and 
                    // Close the stream
                    StreamWriter line = new StreamWriter(file);
                    line.WriteLine(strCmdLine);
                    line.Close();

                    MessageBox.Show("Your batch script has been sucessfully saved.", "Status", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
                }
                catch (Exception)
                {
                    MessageBox.Show("Unable to write to the file. Please make sure that the location has the correct permissions for file writing.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                }

            }
        }

        /// <summary>
        /// Recover the queue from hb_queue_recovery.xml
        /// </summary>
        public void recoverQueue(string file)
        {
            string tempPath = file == "hb_queue_recovery.xml" ? Path.Combine(Path.GetTempPath(), "hb_queue_recovery.xml") : file;

            if (File.Exists(tempPath))
            {
                using (FileStream strm = new FileStream(tempPath, FileMode.Open, FileAccess.Read))
                {
                    if (strm.Length != 0)
                    {
                        List<Job> list = ser.Deserialize(strm) as List<Job>;

                        if (list != null)
                            foreach (Job item in list)
                                queue.Add(item);

                        if (file != "hb_queue_recovery.xml")
                            updateQueueRecoveryFile("hb_queue_recovery.xml");
                    }
                }
            }
        }

        /// <summary>
        /// Check to see if a destination path is already on the queue
        /// </summary>
        /// <param name="destination">Destination path</param>
        /// <returns>Boolean True/False. True = Path Exists</returns>
        public Boolean checkDestinationPath(string destination)
        {
            foreach (Job checkItem in queue)
            {
                if (checkItem.Destination.Contains(destination.Replace("\\\\", "\\")))
                    return true;
            }
            return false;
        }
        #endregion

        #region Encoding

        public Boolean isEncodeStarted { get; private set; }
        public Boolean isPaused { get; private set; }
        public Boolean isEncoding { get; private set; }

        public void startEncode()
        { 
            if (this.count() != 0)
            {
                if (isPaused)
                    isPaused = false;
                else
                {
                    isPaused = false;
                    try
                    {
                        Thread theQueue = new Thread(startProcess) { IsBackground = true };
                        theQueue.Start();
                    }
                    catch (Exception exc)
                    {
                        MessageBox.Show(exc.ToString());
                    }
                }
            }
        }
        public void pauseEncodeQueue()
        {
            isPaused = true;
            EncodePaused(null);
        }
        public void endEncode()
        {
            encodeHandler.closeCLI();
        }

        private void startProcess(object state)
        {
            try
            {
                // Run through each item on the queue
                while (this.count() != 0)
                {
                    string query = getNextJobForEncoding().Query;
                    updateQueueRecoveryFile("hb_queue_recovery.xml"); // Update the queue recovery file

                    encodeHandler.runCli(query);
                    EncodeStarted(null);
                    encodeHandler.hbProcess.WaitForExit();

                    encodeHandler.addCLIQueryToLog(query);
                    encodeHandler.copyLog(lastQueueItem.Destination);

                    encodeHandler.hbProcess.Close();
                    encodeHandler.hbProcess.Dispose();
                    EncodeFinished(null);

                    while (isPaused) // Need to find a better way of doing this.
                    {
                        Thread.Sleep(5000);
                    }
                }
                EncodeQueueFinished(null);

                // After the encode is done, we may want to shutdown, suspend etc.
                encodeHandler.afterEncodeAction();
            }
            catch (Exception exc)
            {
                throw new Exception(exc.ToString());
            }
        }
        #endregion

        #region Events
        public event EventHandler OnEncodeStart;
        public event EventHandler OnPaused;
        public event EventHandler OnEncodeEnded;
        public event EventHandler OnQueueFinished;

        // Invoke the Changed event; called whenever encodestatus changes:
        protected virtual void EncodeStarted(EventArgs e)
        {
            if (OnEncodeStart != null)
                OnEncodeStart(this, e);

            isEncoding = true;
        }
        protected virtual void EncodePaused(EventArgs e)
        {
            if (OnPaused != null)
                OnPaused(this, e);
        }
        protected virtual void EncodeFinished(EventArgs e)
        {
            if (OnEncodeEnded != null)
                OnEncodeEnded(this, e);

            isEncoding = false;
        }
        protected virtual void EncodeQueueFinished(EventArgs e)
        {
            if (OnQueueFinished != null)
                OnQueueFinished(this, e);
        }
        #endregion

    }
}