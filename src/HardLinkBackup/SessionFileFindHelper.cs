﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SharpCompress.Common;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using SharpCompress.Writers.Tar;

namespace HardLinkBackup
{
    internal class SessionFileFindHelper
    {
        private readonly BackupInfo _currentBkp;
        private readonly List<Tuple<BackupFileInfo, BackupInfo>> _prevBackupFiles;
        private readonly Dictionary<long, Dictionary<string, Tuple<BackupFileInfo, BackupInfo>>> _prevBackupFilesLookup;

        public SessionFileFindHelper(BackupInfo currentBkp, List<Tuple<BackupFileInfo, BackupInfo>> prevBackupFiles)
        {
            _currentBkp = currentBkp;
            _prevBackupFiles = prevBackupFiles;

            _prevBackupFilesLookup = prevBackupFiles
                .GroupBy(x => x.Item1.Length)
                .ToDictionary(x => x.Key, x => x.GroupBy(y => y.Item1.Hash).ToDictionary(y => y.Key, y => y.First()));
        }

        public string FindFile(FileInfoEx fInfoEx)
        {
            var fileFromPrevBackup =
                _prevBackupFiles
                    .FirstOrDefault(oldFile =>
                        oldFile.Item1.Length == fInfoEx.FileInfo.Length &&
                        oldFile.Item1.Hash == fInfoEx.FastHashStr);

            string existingFile;
            if (fileFromPrevBackup != null)
                existingFile = fileFromPrevBackup.Item2.AbsolutePath + fileFromPrevBackup.Item1.Path;
            else
            {
                existingFile = _currentBkp.FindFile(fInfoEx)?.Path;

                if (existingFile != null)
                    existingFile = _currentBkp.AbsolutePath + existingFile;
            }

            return existingFile;
        }
    }

    public class TarGzHelper : IDisposable
    {
        private readonly string _filePath;
        private readonly Lazy<TarWriter> _tarContainer;

        private bool _disposed;
        private FileStream _outStream;
        private GZipStream _gzStream;

        public TarGzHelper(string filePath)
        {
            _filePath = filePath;
            _tarContainer = new Lazy<TarWriter>(() =>
            {
                _outStream = File.Create(_filePath);
                _gzStream = new GZipStream(_outStream, CompressionMode.Compress, CompressionLevel.BestSpeed);
                var tarArchive = new TarWriter(_gzStream, new TarWriterOptions(CompressionType.None, true)
                {
                    ArchiveEncoding = new ArchiveEncoding
                    {
                        Default = Encoding.UTF8,
                        Forced = Encoding.UTF8,
                    },
                });

                return tarArchive;
            });
        }

        public void AddFile(string fileName, Stream file)
        {
            _tarContainer.Value.Write(fileName, file, null);
        }
        
        public void AddEmptyFolder(string folderName)
        {
            _tarContainer.Value.WriteEmptyFolder(folderName);
        }

        public bool IsArchiveCreated
        {
            get { return _tarContainer.IsValueCreated; }
        }

        private void ReleaseUnmanagedResources()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                if (IsArchiveCreated)
                {
                    _tarContainer.Value.Dispose();
                    _gzStream.Dispose();
                    _outStream.Dispose();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        public void Dispose()
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        ~TarGzHelper()
        {
            ReleaseUnmanagedResources();
        }
    }
}