﻿using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess.Memory.Unix
{
    internal sealed class MemoryFileUnix : IMemoryFile
    {
        private const string Folder = ".cloudtoid/interprocess/mmf";
        private const string FileExtension = ".qu";
        private readonly string filePath;
        private readonly bool mustDeleteFileOnDispose;
        private readonly ILogger<MemoryFileUnix> logger;

        internal MemoryFileUnix(QueueOptions options, ILoggerFactory loggerFactory)
        {
            logger = loggerFactory.CreateLogger<MemoryFileUnix>();
            filePath = Path.Combine(options.Path, Folder);
            Directory.CreateDirectory(filePath);
            filePath = Path.Combine(filePath, options.QueueName + FileExtension);

            FileStream stream;

            try
            {
                // assume that the file doesn't exist and try and create it

                stream = new FileStream(
                    filePath,
                    FileMode.CreateNew,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 0x1000,
                    FileOptions.DeleteOnClose);

                mustDeleteFileOnDispose = true;
            }
            catch (IOException)
            {
                stream = new FileStream(
                    filePath,
                    options.CreateOrOverride ? FileMode.Create : FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 0x1000,
                    FileOptions.None);

                mustDeleteFileOnDispose = options.CreateOrOverride;
            }

            try
            {
                MappedFile = MemoryMappedFile.CreateFromFile(
                    stream,
                    mapName: null, // do not set this or it will not work on Linux/Unix/MacOS
                    options.BytesCapacity,
                    MemoryMappedFileAccess.ReadWrite,
                    HandleInheritability.None,
                    false);
            }
            catch
            {
                // do not leave any resources hanging

                try
                {
                    stream.Dispose();
                }
                catch
                {
                    ResetBackingFile();
                }

                throw;
            }
        }

        ~MemoryFileUnix()
           => Dispose(false);

        public MemoryMappedFile MappedFile { get; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                    MappedFile.Dispose();
            }
            finally
            {
                ResetBackingFile();
            }
        }

        /// <summary>
        /// Deletes the backing file if it was created by this instance of <see cref="MemoryFileUnix"/>.
        /// </summary>
        private void ResetBackingFile()
        {
            if (mustDeleteFileOnDispose && !Util.TryDeleteFile(filePath))
                logger.LogError("Failed to delete queue's shared memory backing file.");
        }
    }
}
