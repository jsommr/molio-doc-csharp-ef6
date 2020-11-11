using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Threading;
using static System.Data.SQLite.SQLiteConnectionEventType;


namespace MolioDocEF6
{
    /// <summary>
    /// Entity Framework 6 does not correctly close all SqlCommand's. That means the SQLite file is inaccessible until the
    /// garbage collector closes them. This fix takes care of closing all commands immediately after SQLiteConnection is
    /// closed.
    /// 
    /// It's copied from https://stackoverflow.com/a/38268171/1193236 and altered with AsyncLocal to make it thread safe and
    /// safe to use with async code. It must be run once, and only once, preferably at application startup, before using
    /// SQLiteConnection.
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
        public static void Initialise()
        {
            SQLiteConnection.Changed += SQLiteConnectionChanged;
        }

        static readonly AsyncLocal<List<SQLiteCommand>> OpenCommands = new AsyncLocal<List<SQLiteCommand>>();

        static List<SQLiteCommand> Instance() => OpenCommands.Value ?? (OpenCommands.Value = new List<SQLiteCommand>());

        static void Add(SQLiteCommand command) => Instance().Add(command);

        static void Remove(SQLiteCommand command) => Instance().Remove(command);

        static List<SQLiteCommand> CopyOpenCommands() => Instance().ToList();

        static void SQLiteConnectionChanged(object sender, ConnectionEventArgs eventArgs)
        {
            if (eventArgs.EventType == NewCommand && eventArgs.Command is SQLiteCommand newCommand)
            {
                // Whenever a SQLiteCommand is executed, it's added to the list of open connections
                Add(newCommand);
            }
            else if (eventArgs.EventType == DisposingCommand && eventArgs.Command is SQLiteCommand disposedCommand)
            {
                // Entity Framework sometimes remembers to dispose a command, in that case it can be removed
                Remove(disposedCommand);
            }

            if (eventArgs.EventType == Closed)
            {
                // Wrap up and close all commands (with a copy of the list because it's changed in the loop)
                foreach (var command in CopyOpenCommands())
                {
                    command.Connection = null;
                    Remove(command);
                }
            }
        }
    }
}
