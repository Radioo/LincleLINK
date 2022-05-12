using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace LincleLINK
{
    public class Instance
    {
        public string Name { get; set; }
        public long TotalFileSize { get; set; }
        public int TotalFileCount { get; set; }
        public string TotalFileSizeString { get; set; }
        public List<InstanceFile> FileList { get; set; }
        public List<string> DirectoryList { get; set; }

        public Instance(List<InstanceFile> fileList, List<string> directoryList, long totalFileSize, int totalFileCount, string name)
        {
            Name = name;
            FileList = fileList;
            DirectoryList = directoryList;
            TotalFileSize = totalFileSize;
            TotalFileCount = totalFileCount;
            TotalFileSizeString = ReadableSize(totalFileSize);
        }

        public static string ReadableSize(long size)
        {
            if (size < 1024f)
            {
                return $"{size} B";
            }
            else if (size > 1024f && size < 1048576f)
            {
                return $"{Math.Round(size / 1024f, 2)} KB";
            }
            else if (size > 1048576f && size < 1073741824f)
            {
                return $"{Math.Round(size / 1048576f, 2)} MB";
            }
            else if (size > 1073741824f)
            {
                return $"{Math.Round(size / 1073741824f, 2)} GB";
            }
            else return $"{Math.Round(size / 1099511627776f, 2)} TB";
        }

        public void SaveToFile()
        {
            File.WriteAllText(Path.Combine(MainWindowLogic.instanceDir, Name + ".json"), JsonSerializer.Serialize(this));
        }

        public void SaveToFile(JsonSerializerOptions options)
        {
            File.WriteAllText(Path.Combine(MainWindowLogic.instanceDir, Name + ".json"), JsonSerializer.Serialize(this, options));
        }
    }

    public class InstanceFile
    {
        public string FileName { get; set; }
        public string RelativePath { get; set; }
        public long FileSize { get; set; }
        public string HashedFileName { get; set; }

        public InstanceFile(string fileName, string relativePath, long fileSize, string hashedFileName)
        {
            FileName = fileName;
            RelativePath = relativePath;
            FileSize = fileSize;
            HashedFileName = hashedFileName;
        }
    }

    public class InstanceListEntry
    {
        public string InstanceName { get; set; }
        public int FileCount { get; set; }
        public long TotalFileSize { get; set; }
        public string TotalFileSizeString { get; set; }

        public InstanceListEntry(Instance inst)
        {
            InstanceName = inst.Name;
            FileCount = inst.FileList.Count;
            TotalFileSize = inst.TotalFileSize;
            TotalFileSizeString = inst.TotalFileSizeString;
        }
    }

    public struct PassedFileInfo
    {
        public string FileName { get; set; }
        public string HashedFileName { get; set; }

        public PassedFileInfo(string fileName, string hashedFileName)
        {
            FileName = fileName;
            HashedFileName = hashedFileName;
        }
    }

    public class TorrentPiecer
    {
        // main list of pieces
        public List<byte[]> Pieces { get; set; }

        // properties for file-to-piece mapping
        public Dictionary<PassedFileInfo, HashSet<long>> FilePieceMap { get; set; }
        public HashSet<long> CurrentFilePieces { get; set; }

        // imported values through the constructor
        private long TotalFileSize { get; set; }
        private long PieceSize { get; set; }
        private long NumberOfPieces { get; set; }

        // working properties
        private byte[]? CurrentPiece { get; set; } // current piece being worked on
        private long CurrentPieceBytesLeft { get; set; } // free bytes remaining in the current piece
        private long CurrentPieceIndex { get; set; } // starts at 0
        private long CurrentFileBytesLeft { get; set; } // number of remaining bytes in the current file to work through
        private string CurrentFileName { get; set; } // name of the current file
        private long LastPieceSize { get; set; } // size of the last piece, calculated in the constructor

        public TorrentPiecer(long totalFileSize, long pieceSize, long numberOfPieces)
        {
            TotalFileSize = totalFileSize;
            PieceSize = pieceSize;
            NumberOfPieces = numberOfPieces;

            Pieces = new();
            FilePieceMap = new();

            CurrentPieceIndex = 0;

            // calculate the last piece size
            LastPieceSize = TotalFileSize - ((NumberOfPieces - 1) * PieceSize);
        }

        public void PushFile(string fileName, string hashedFileName, byte[] fileBytes)
        {
            CurrentFileBytesLeft = fileBytes.Length;
            CurrentFileName = fileName;
            CurrentFilePieces = new();
            WorkBytes(fileBytes);
            PassedFileInfo pInfo = new(fileName, hashedFileName);
            FilePieceMap.Add(pInfo, CurrentFilePieces);
        }

        private void WorkBytes(byte[] fileBytes)
        {
            // work through the file
            while (CurrentFileBytesLeft > 0)
            {
                if (CurrentPiece == null)
                {
                    CreatePiece();
                }

                CurrentFilePieces.Add(CurrentPieceIndex);

                if (CurrentPieceBytesLeft > CurrentFileBytesLeft)
                {
                    Array.Copy(fileBytes, fileBytes.Length - CurrentFileBytesLeft, CurrentPiece,
                        CurrentPiece.Length - CurrentPieceBytesLeft, CurrentFileBytesLeft);
                    CurrentPieceBytesLeft -= CurrentFileBytesLeft;
                    CurrentFileBytesLeft = 0;
                }
                else if (CurrentPieceBytesLeft < CurrentFileBytesLeft)
                {
                    Array.Copy(fileBytes, fileBytes.Length - CurrentFileBytesLeft, CurrentPiece,
                        CurrentPiece.Length - CurrentPieceBytesLeft, CurrentPieceBytesLeft);
                    CurrentFileBytesLeft -= CurrentPieceBytesLeft;
                    HashPiece();
                }
                else if (CurrentPieceBytesLeft == CurrentFileBytesLeft)
                {
                    Array.Copy(fileBytes, fileBytes.Length - CurrentFileBytesLeft, CurrentPiece,
                        CurrentPiece.Length - CurrentPieceBytesLeft, CurrentPieceBytesLeft);
                    CurrentFileBytesLeft = 0;
                    HashPiece();
                }
            }
        }

        private void CreatePiece()
        {           
            if (CurrentPieceIndex == NumberOfPieces - 1)
            {
                CurrentPiece = new byte[LastPieceSize];
                CurrentPieceBytesLeft = LastPieceSize;
            }
            else
            {
                CurrentPiece = new byte[PieceSize];
                CurrentPieceBytesLeft = PieceSize;
            }
        }

        private void HashPiece()
        {
            SHA1 sha = SHA1.Create();
            byte[] hash = sha.ComputeHash(CurrentPiece);
            Pieces.Add(hash);
            CurrentPiece = null;
            CurrentPieceIndex++;
        }
    }
}
