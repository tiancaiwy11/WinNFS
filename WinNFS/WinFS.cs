﻿using System;
using NFSLibrary;
using DokanNet;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Terdos.WinNFS
{
    public class NFSProxy : IDokanOperations
    {
        private NFSClient nfsClient;

        #region Debugging

        private string ToTrace(DokanFileInfo info)
        {
            var context = info.Context != null ? "<" + info.Context.GetType().Name + ">" : "<null>";

            return string.Format(CultureInfo.InvariantCulture, "{{{0}, {1}, {2}, {3}, {4}, #{5}, {6}, {7}}}",
                context, info.DeleteOnClose, info.IsDirectory, info.NoCache, info.PagingIo, info.ProcessId, info.SynchronousIo, info.WriteToEndOfFile);
        }

        private string ToTrace(DateTime? date)
        {
            return date.HasValue ? date.Value.ToString(CultureInfo.CurrentCulture) : "<null>";
        }

        private NtStatus Trace(string method, string fileName, DokanFileInfo info, NtStatus result, params string[] parameters)
        {
            var extraParameters = parameters != null && parameters.Length > 0 ? ", " + string.Join(", ", parameters) : string.Empty;

#if TRACE
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}('{1}', {2}{3}) -> {4}",
                method, fileName, ToTrace(info), extraParameters, result));
#endif

            return result;
        }

        private NtStatus Trace(string method, string fileName, DokanFileInfo info,
                                  DokanNet.FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes,
                                  NtStatus result)
        {
#if TRACE
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0}('{1}', {2}, [{3}], [{4}], [{5}], [{6}], [{7}]) -> {8}",
                 method, fileName, ToTrace(info), access, share, mode, options, attributes, result));
#endif

            return result;
        }

        private void Debug(string format, params object[] args)
        {
#if TRACE
            Console.Error.WriteLine("NFS: " + format, args);
            System.Diagnostics.Debug.WriteLine(string.Format("NFS: " + format, args));
#endif
        }


        #endregion

        string CleanFileName(string filename)
        {
            int columnIndex = filename.IndexOf(":");
            if (columnIndex == -1)
                return filename;

            return filename.Substring(0, columnIndex);
        }


        public NFSProxy(System.Net.IPAddress address)
        {
            nfsClient = new NFSClient(NFSClient.NFSVersion.v3);
            nfsClient.Connect(address, 500,500,600000);
        }

        public List<String> GetExportedDevices()
        {
            return nfsClient.GetExportedDevices();
        }

        public void Mount(String device, String path )//, DokanNet.DokanOptions d, int number)
        {
            /*
            DokanOptions dokanOptions = new DokanOptions();
            dokanOptions.DebugMode = DebugMode;
            dokanOptions.NetworkDrive = DiskOrFolder;
            dokanOptions.MountPoint = MountPoint;
            dokanOptions.UseKeepAlive = true;
            dokanOptions.UseAltStream = true;
            dokanOptions.VolumeLabel = strDriveLabel;
            dokanOptions.ThreadCount = 1;
            */

            nfsClient.MountDevice(device);
            Dokan.Mount(this, path, DokanOptions.FixedDrive | DokanOptions.DebugMode, 1);
        }

        //

        void IDokanOperations.Cleanup(string fileName, DokanFileInfo info)
        {
            Debug("Cleanup");
            nfsClient.CompleteIO();
            //throw new NotImplementedException();
        }

        void IDokanOperations.CloseFile(string fileName, DokanFileInfo info)
        {
            Debug("CloseFile");
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.CreateFile(string fileName, DokanNet.FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, DokanFileInfo info)
        {
            fileName = CleanFileName(fileName);

            try
            {
                Debug("CreateFile {0}", fileName);

                string Directory = nfsClient.GetDirectoryName(fileName);
                string FileName = nfsClient.GetFileName(fileName);
                string FullPath = nfsClient.Combine(FileName, Directory);
                
                if (nfsClient.IsDirectory(FullPath))
                    return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.Success);

                switch (mode)
                {
                    case FileMode.Open:
                        {
                            Debug("Open");
                            if (!nfsClient.FileExists(FullPath))
                                return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
                            break;
                        }
                    case FileMode.CreateNew:
                        {
                            Debug("CreateNew");
                            if (nfsClient.FileExists(FullPath))
                                return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.AlreadyExists);
                            else
                                if (info.IsDirectory)
                                    nfsClient.CreateDirectory(FullPath);
                                else
                                    nfsClient.CreateFile(FullPath);
                            break;
                        }
                    case FileMode.Create:
                        {
                            Debug("Create");
                            if (nfsClient.FileExists(FullPath))
                                nfsClient.DeleteFile(FullPath);

                            nfsClient.CreateFile(FullPath);
                            break;
                        }
                    case FileMode.OpenOrCreate:
                        {
                            Debug("OpenOrCreate");
                            if (!nfsClient.FileExists(FullPath))
                                nfsClient.CreateFile(FullPath);
                            break;
                        }
                    case FileMode.Truncate:
                        {
                            Debug("Truncate");
                            if (!nfsClient.FileExists(FullPath))
                                return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
                            else
                                nfsClient.CreateFile(FullPath);
                            break;
                        }
                    case FileMode.Append:
                        {
                            Debug("Append");
                            if (!nfsClient.FileExists(FullPath))
                                return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.FileNotFound);
                            break;
                        }
                    default:
                        {
                            Debug("Error unknown FileMode {0}", mode);
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Debug("CreateFile file {0} exception {1}", fileName, ex.Message);
                return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.Error);
            }
            return Trace("CreateFile", fileName, info, access, share, mode, options, attributes, DokanResult.Success);
        }

        NtStatus IDokanOperations.DeleteDirectory(string fileName, DokanFileInfo info)
        {
            Debug("DeleteDirectory");

            fileName = CleanFileName(fileName);

            try
            {
                Debug("DeleteDirectory {0}", fileName);
                string Directory = nfsClient.GetDirectoryName(fileName);
                string FileName = nfsClient.GetFileName(fileName);
                string FullPath = nfsClient.Combine(FileName, Directory);

                nfsClient.DeleteDirectory(FullPath);
            }
            catch (Exception ex)
            {
                Debug("DeleteDirectory file {0} exception {1}", fileName, ex.Message);
                return Trace("DeleteDirectory", fileName, info, DokanResult.Error);
            }
            return Trace("DeleteDirectory", fileName, info, DokanResult.Success);
        }

        NtStatus IDokanOperations.DeleteFile(string fileName, DokanFileInfo info)
        {
            Debug("DeleteFile");

            fileName = CleanFileName(fileName);

            try
            {
                Debug("DeleteFile {0}", fileName);
                string Directory = nfsClient.GetDirectoryName(fileName);
                string FileName = nfsClient.GetFileName(fileName);
                string FullPath = nfsClient.Combine(FileName, Directory);

                if (nfsClient.FileExists(FullPath))
                {
                    nfsClient.DeleteFile(FullPath);
                }
                else
                {
                    return Trace("DeleteFile", fileName, info, DokanResult.FileNotFound);
                }
            }
            catch (Exception ex)
            {
                Debug("DeleteFile file {0} exception {1}", fileName, ex.Message);
                return Trace("DeleteFile", fileName, info, DokanResult.Error);
            }

            return Trace("DeleteFile", fileName, info, DokanResult.Success);
        }

        NtStatus IDokanOperations.FindFiles(string fileName, out IList<FileInformation> files, DokanFileInfo info)
        {
            Debug("FindFiles");

            fileName = CleanFileName(fileName);
            files = new List<FileInformation>();

            try
            {
                Debug("FindFiles {0}", fileName);
                string Directory = nfsClient.GetDirectoryName(fileName);
                string FileName = nfsClient.GetFileName(fileName);
                string FullPath = nfsClient.Combine(FileName, Directory);

                foreach (string strItem in nfsClient.GetItemList(FullPath))
                {
                    Debug("Found: {0}", strItem);
                    NFSLibrary.Protocols.Commons.NFSAttributes nfsAttributes = nfsClient.GetItemAttributes(nfsClient.Combine(strItem, FullPath));
                    if (nfsAttributes != null)
                    {
                        FileInformation fi = new FileInformation();
                        fi.Attributes = nfsAttributes.NFSType == NFSLibrary.Protocols.Commons.NFSItemTypes.NFDIR ? System.IO.FileAttributes.Directory : System.IO.FileAttributes.Normal;
                        fi.CreationTime = nfsAttributes.CreateDateTime;
                        fi.LastAccessTime = nfsAttributes.LastAccessedDateTime;
                        fi.LastWriteTime = nfsAttributes.ModifiedDateTime;
                        fi.Length = (long)nfsAttributes.Size;
                        fi.FileName = strItem;
                        files.Add(fi);
                    }
                }
            }
            catch (Exception ex)
            {                
                Debug("FindFiles file {0} exception {1}", fileName, ex.Message);
                return Trace("FindFiles", fileName, info, DokanResult.Error);
            }

            return Trace("FindFiles", fileName, info, DokanResult.Success);
        }

        NtStatus IDokanOperations.FindStreams(string fileName, out IList<FileInformation> streams, DokanFileInfo info)
        {
            Debug("FindStreams");
            streams = new List<FileInformation>();
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.FlushFileBuffers(string fileName, DokanFileInfo info)
        {
            Debug("FlushFileBuffers");
            return NtStatus.Success;
            //throw new NotImplementedException();

        }

        NtStatus IDokanOperations.GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, DokanFileInfo info)
        {
            Debug("GetDiskFreeSpace");
            freeBytesAvailable = 1024L * 1024 * 1024 * 10;
            totalNumberOfBytes = 1024L * 1024 * 1024 * 20;
            totalNumberOfFreeBytes = 1024L * 1024 * 1024 * 10;
            return NtStatus.Success;
        }

        NtStatus IDokanOperations.GetFileInformation(string fileName, out FileInformation fileInfo, DokanFileInfo info)
        {
            fileName = CleanFileName(fileName);
            fileInfo = new FileInformation();

            try
            {
                Debug("GetFileInformation {0}", fileName);
                string Directory = nfsClient.GetDirectoryName(fileName);
                string FileName = nfsClient.GetFileName(fileName);
                string FullPath = nfsClient.Combine(FileName, Directory);

                NFSLibrary.Protocols.Commons.NFSAttributes nfsAttributes = nfsClient.GetItemAttributes(FullPath);
                if (nfsAttributes == null)
                    return Trace("GetFileInformation", fileName, info, DokanResult.Error);

                if (nfsAttributes.NFSType == NFSLibrary.Protocols.Commons.NFSItemTypes.NFDIR)
                    fileInfo.Attributes = System.IO.FileAttributes.Directory;
                else
                    fileInfo.Attributes = System.IO.FileAttributes.Archive;

                fileInfo.LastAccessTime = nfsAttributes.LastAccessedDateTime;
                fileInfo.LastWriteTime = nfsAttributes.ModifiedDateTime;
                fileInfo.CreationTime = nfsAttributes.CreateDateTime;
                fileInfo.Length = (long)nfsAttributes.Size;
                fileInfo.FileName = FileName;
                Debug("GetFileInformation {0},{1},{2}", fileInfo.FileName, fileInfo.Length, fileInfo.Attributes);
            }
            catch (Exception ex)
            {
                Debug("GetFileInformation file {0} exception {1}", fileName, ex.Message);
                return Trace("GetFileInformation", fileName, info, DokanResult.Error);
            }

            return Trace("GetFileInformation", fileName, info, DokanResult.Success);
        }

        NtStatus IDokanOperations.GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections, DokanFileInfo info)
        {
            Debug("GetFileSecurity");
            security = null;
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, DokanFileInfo info)
        {
            Debug("GetVolumeInformation");
            volumeLabel = "DOKAN";
            fileSystemName = "NTFS";

            features = FileSystemFeatures.CasePreservedNames | FileSystemFeatures.CaseSensitiveSearch |
                       FileSystemFeatures.PersistentAcls | FileSystemFeatures.SupportsRemoteStorage |
                       FileSystemFeatures.UnicodeOnDisk;

            return Trace("GetVolumeInformation", null, info, DokanResult.Success, "out " + volumeLabel, "out " + features.ToString(), "out " + fileSystemName);

        }

        NtStatus IDokanOperations.LockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            Debug("LockFile");
            return NtStatus.Success;
            //throw new NotImplementedException();

        }

        NtStatus IDokanOperations.Mounted(DokanFileInfo info)
        {
            Debug("Mounted");

            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.MoveFile(string oldName, string newName, bool replace, DokanFileInfo info)
        {
            Debug("MoveFile");

            oldName = CleanFileName(oldName);

            try
            {
                Debug("MoveFile {0}", oldName);

                if (nfsClient.IsDirectory(newName))
                {
                    newName = nfsClient.Combine(
                                    nfsClient.GetFileName(oldName),
                                    newName
                                );
                }

                nfsClient.Move(oldName, newName);
            }
            catch (Exception ex)
            {
                Debug("MoveFile file {0} newfile {1} exception {2}", oldName, newName, ex.Message);
                return Trace("MoveFile", oldName, info, DokanResult.Error, newName, replace.ToString(CultureInfo.InvariantCulture));
            }

            return Trace("MoveFile", oldName, info, DokanResult.Success, newName, replace.ToString(CultureInfo.InvariantCulture));
        }

        NtStatus IDokanOperations.ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, DokanFileInfo info)
        {
            Debug("ReadFile");

            fileName = CleanFileName(fileName);
            bytesRead = 0;

            if (nfsClient.IsDirectory(fileName))
                return Trace("ReadFile", fileName, info, DokanResult.Success, "out: 0 ");

            try
            {
                Debug("ReadFile {0}", fileName);
                string Directory = nfsClient.GetDirectoryName(fileName);
                string FileName = nfsClient.GetFileName(fileName);
                string FullPath = nfsClient.Combine(FileName, Directory);

                Debug("ReadFile {0} {1} {2} {3}", Directory, FileName, offset, buffer.Length);
                long Bytes = (long)buffer.Length;
                nfsClient.Read(FullPath, (long)offset, ref Bytes, ref buffer);
                if (Bytes != -1)
                {
                    bytesRead = (int)Bytes;
                    Debug("ReadFile bytes {0}", bytesRead);
                }
                else
                    return Trace("ReadFile", fileName, info, DokanResult.Error, "out: 0");
            }
            catch (Exception ex)
            {
                Debug("ReadFile file {0} exception {1}", fileName, ex.Message);
                return Trace("ReadFile", fileName, info, DokanResult.Error, "out: 0");
            }

            return Trace("ReadFile", fileName, info, DokanResult.Success, "out " + bytesRead.ToString(), offset.ToString(CultureInfo.InvariantCulture));
        }

        NtStatus IDokanOperations.SetAllocationSize(string fileName, long length, DokanFileInfo info)
        {
            Debug("SetAllocationSize");

            fileName = CleanFileName(fileName);

            try
            {
                Debug("SetEndOfFile {0}", fileName);
                string Directory = nfsClient.GetDirectoryName(fileName);
                string FileName = nfsClient.GetFileName(fileName);
                string FullName = nfsClient.Combine(FileName, Directory);

                NFSLibrary.Protocols.Commons.NFSAttributes attr = nfsClient.GetItemAttributes(FullName);
                if (attr.Size < length)
                    nfsClient.SetFileSize(FullName, length);
            }
            catch (Exception ex)
            {
                Debug("SetEndOfFile file {0} newfile {1} exception {2}", fileName, ex.Message);
                return Trace("SetAllocationSize", fileName, info, DokanResult.Error, length.ToString(CultureInfo.InvariantCulture));
            }

            return Trace("SetAllocationSize", fileName, info, DokanResult.Success, length.ToString(CultureInfo.InvariantCulture));
        }

        NtStatus IDokanOperations.SetEndOfFile(string fileName, long length, DokanFileInfo info)
        {
            Debug("SetEndOfFile");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.SetFileAttributes(string fileName, FileAttributes attributes, DokanFileInfo info)
        {
            Debug("SetFileAttributes");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, DokanFileInfo info)
        {
            Debug("SetFileSecurity");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, DokanFileInfo info)
        {
            Debug("SetFileTime");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.UnlockFile(string fileName, long offset, long length, DokanFileInfo info)
        {
            Debug("UnlockFile");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.Unmounted(DokanFileInfo info)
        {
            Debug("Unmounted");
            return NtStatus.Success;
            //throw new NotImplementedException();
        }

        NtStatus IDokanOperations.WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, DokanFileInfo info)
        {
            Debug("WriteFile");

            bytesWritten = 0;


            fileName = CleanFileName(fileName);

            try
            {
                Debug("WriteFile {0}", fileName);
                string Directory = nfsClient.GetDirectoryName(fileName);
                string FileName = nfsClient.GetFileName(fileName);
                string FullPath = nfsClient.Combine(FileName, Directory);

                Debug("WriteFile {0} {1} {2} {3}", Directory, FileName, offset, buffer.Length);
                UInt32 Bytes = 0;
                nfsClient.Write(FullPath, (long)offset, (uint)buffer.Length, buffer, out Bytes);
                if (Bytes != 0)
                {
                    bytesWritten = (int)Bytes;
                    Debug("WriteFile bytes {0}", bytesWritten);
                }
            }
            catch (Exception ex)
            {
                Debug("WriteFile file {0} exception {1}", fileName, ex.Message);
                return Trace("WriteFile", fileName, info, DokanResult.Error, "out " + bytesWritten.ToString(), offset.ToString(CultureInfo.InvariantCulture));
            }

            return Trace("WriteFile", fileName, info, DokanResult.Success, "out " + bytesWritten.ToString(), offset.ToString(CultureInfo.InvariantCulture));
        }
    }
}
