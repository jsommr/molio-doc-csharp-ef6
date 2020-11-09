using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.Linq;
using System.Threading;

namespace MolioDocEF6
{
    /// <summary>
    /// Entity Framework 6 does not correctly close all SqlCommand's. That means the SQLite file is inaccessible until the
    /// garbage collector closes them. This fix takes care of closing all commands immediately after SQLiteConnection is
    /// closed.
    /// 
    /// It's copied from https://stackoverflow.com/a/38268171/1193236 and altered with ThreadLocal to make it thread safe.
    /// It must be run once, and only once, preferably at application startup, before using SQLiteConnection.
    /// 
    /// You might not need this fix if you only issue write commands. Unclosed commands seems to only be a problem
    /// when reading data.
    /// 
    /// I've tested for memory leaks in this script by creating 60.000 databases in parallel on four cores using both read
    /// and write commands with Entity Framework 6. Memory usage maxed out at 128mb, with regular garbage collection cleaning
    /// up things.
    /// 
    /// There's another "fix" for this problem, one that I'd rather call a hack:
    /// 
    ///   GC.Collect();
    ///   GC.WaitForPendingFinalizers();
    ///   
    /// If calling this after closing a SQLiteConnection, the SqlCommand's are closed and the SQLite file is released. This
    /// is a total no-go if you're writing a web application, but might be fine for desktop applications.
    /// </summary>
    public static class SQLiteEF6Fix
    {
        static readonly ThreadLocal<List<SQLiteCommand>> OpenCommands = new ThreadLocal<List<SQLiteCommand>>(() => new List<SQLiteCommand>());

        public static void Initialise()
        {
            SQLiteConnection.Changed += SqLiteConnectionOnChanged;
        }

        static void SqLiteConnectionOnChanged(object sender, ConnectionEventArgs connectionEventArgs)
        {
            if (connectionEventArgs.EventType == SQLiteConnectionEventType.NewCommand && connectionEventArgs.Command is SQLiteCommand)
            {
                OpenCommands.Value.Add((SQLiteCommand)connectionEventArgs.Command);
            }
            else if (connectionEventArgs.EventType == SQLiteConnectionEventType.DisposingCommand && connectionEventArgs.Command is SQLiteCommand)
            {
                try
                {
                    OpenCommands.Value.Remove((SQLiteCommand)connectionEventArgs.Command);
                }
                catch (ObjectDisposedException)
                {
                    // OpenCommands have been disposed. The thread is gone, and there's nothing to do.
                }
            }

            if (connectionEventArgs.EventType == SQLiteConnectionEventType.Closed)
            {
                var commands = OpenCommands.Value.ToList();
                foreach (var cmd in commands)
                {
                    if (cmd.Connection == null)
                    {
                        OpenCommands.Value.Remove(cmd);
                    }
                    else if (cmd.Connection.State == ConnectionState.Closed)
                    {
                        cmd.Connection = null;
                        OpenCommands.Value.Remove(cmd);
                    }
                }
            }
        }
    }
}
