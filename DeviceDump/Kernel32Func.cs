using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;


namespace DeviceDump
{
    class Kernel32Func
    {
        /* === CONSTANTES === */

        /* taille de bloc utilisée pour une opération de copie "atomique" */
        private const int COPY_BLOCK_SIZE = 1024 * 1024;


        /* === MÉTHODES UTILITAIRES PRIVÉES (STATIQUES) === */

        private unsafe static void LockVolume(SafeFileHandle hVolume) {
            int ret;
            bool ok = DeviceIoControl(hVolume,
                                      IoControlCode.FsctlLockVolume,
                                      null,
                                      0,
                                      null,
                                      0,
                                      out ret,
                                      (IntPtr)null);
            if (!ok) {
                int errNum = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                        errNum,
                        "DeviceIOControl(FSCTL_LOCK_VOLUME) failed!");
            }
        }

        private unsafe static void UnlockVolume(SafeFileHandle hVolume) {
            int ret;
            bool ok = DeviceIoControl(hVolume,
                                      IoControlCode.FsctlUnlockVolume,
                                      null,
                                      0,
                                      null,
                                      0,
                                      out ret,
                                      (IntPtr)null);
            if (!ok) {
                int errNum = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                        errNum,
                        "DeviceIOControl(FSCTL_UNLOCK_VOLUME) failed!");
            }
        }

        private unsafe static bool VolumeIsMounted(SafeFileHandle hVolume) {
            int ret;
            return DeviceIoControl(hVolume,
                                   IoControlCode.FsctlIsVolumeMounted,
                                   null,
                                   0,
                                   null,
                                   0,
                                   out ret,
                                   (IntPtr)null);
        }

        private unsafe static void DismountVolume(SafeFileHandle hVolume) {
            int ret;
            bool ok = DeviceIoControl(hVolume,
                                      IoControlCode.FsctlDismountVolume,
                                      null,
                                      0,
                                      null,
                                      0,
                                      out ret,
                                      (IntPtr)null);
            if (!ok) {
                int errNum = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                        errNum,
                        "DeviceIOControl(FSCTL_DISMOUNT_VOLUME) failed!");
            }
        }

        private unsafe static bool CheckVerifyStorage(SafeFileHandle hVolume) {
            int ret;
            return DeviceIoControl(hVolume,
                                   IoControlCode.IoctlStorageCheckVerify,
                                   null,
                                   0,
                                   null,
                                   0,
                                   out ret,
                                   (IntPtr)null);
        }

        private unsafe static bool CheckVerifyStorage2(SafeFileHandle hVolume) {
            int ret;
            return DeviceIoControl(hVolume,
                                   IoControlCode.IoctlStorageCheckVerify2,
                                   null,
                                   0,
                                   null,
                                   0,
                                   out ret,
                                   (IntPtr)null);
        }

        private unsafe static uint GetDeviceNumber(SafeFileHandle hVolume) {
            StorageDeviceNumber diskNumber;
            int ret;
            bool ok = DeviceIoControl(hVolume,
                                      IoControlCode.IoctlStorageGetDeviceNumber,
                                      null,
                                      0,
                                      &diskNumber,
                                      sizeof(StorageDeviceNumber),
                                      out ret,
                                      (IntPtr)null);
            if (!ok) {
                int errNum = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                        errNum,
                        "DeviceIOControl(IOCTL_STORAGE_GET_DEVICE_NUMBER) failed!");
            }
            return diskNumber.DeviceNumber;
        }

        private unsafe static long GetLengthInfo(SafeFileHandle hDevice) {
            long deviceLength;
            int ret;
            bool ok = DeviceIoControl(hDevice,
                                      IoControlCode.IoctlDiskGetLengthInfo,
                                      null,
                                      0,
                                      &deviceLength,
                                      sizeof(long),
                                      out ret,
                                      (IntPtr)null);
            if (!ok) {
                int errNum = Marshal.GetLastWin32Error();
                throw new Win32Exception(
                        errNum,
                        "DeviceIOControl(IOCTL_DIK_GET_LENGTH_INFO) failed!");
            }
            return deviceLength;
        }


        private unsafe static void CopyBlockFromVolume(SafeFileHandle hVolume,
                                                       FileStream destFile,
                                                       int byteCount)
        {
            byte[] buffer = new byte[COPY_BLOCK_SIZE];
            int bytesRead;
            bool ok;
            fixed (byte* pb = buffer) {
                ok = ReadFile(hVolume,
                              pb,
                              byteCount,
                              out bytesRead,
                              (IntPtr)null);
            }
            if ((!ok) || (bytesRead < byteCount)) {
                int errNum = Marshal.GetLastWin32Error();
                throw new ApplicationException(
                    String.Format("Could not read {0} bytes from source" +
                                  " ({1} bytes read)!",
                                  byteCount, bytesRead),
                    new Win32Exception(errNum));
            }
            try {
                destFile.Write(buffer, 0, bytesRead);
            } catch (Exception exc) {
                throw new ApplicationException(
                        String.Format("Could not write {0} bytes to image file!",
                                      bytesRead),
                        exc);
            }
        }

        private static void DumpDevice(SafeFileHandle hDevice,
                                       string destFilePath,
                                       long srcVolSize,
                                       ReportProgress rpMethod)
        {
            long bytesToCopy = srcVolSize;
            /* ouvre le fichier image destination en écriture */
            using (FileStream destFile = File.Open(destFilePath,
                                                   FileMode.Create,
                                                   FileAccess.Write,
                                                   FileShare.None))
            {
                /* recopie le volume / disque source sur
                   le fichier destination bloc par bloc */
                while (bytesToCopy > COPY_BLOCK_SIZE) {
                    CopyBlockFromVolume(hDevice, destFile, COPY_BLOCK_SIZE);
                    bytesToCopy -= COPY_BLOCK_SIZE;
                    if (rpMethod != null) {
                        rpMethod(100 - (int)(100 * bytesToCopy
                                                 / srcVolSize));
                    }
                }
                /* traite l'éventuel ultime bloc (tronqué) */
                if (bytesToCopy > 0) {
                    CopyBlockFromVolume(hDevice, destFile, (int)bytesToCopy);
                    bytesToCopy = 0;
                    rpMethod(100);
                }
                destFile.Flush(true);
            }
            /* quitter 'using' referme le fichier image destination */
        }


        /* === DÉLÉGATIONS === */

        /// <summary>
        /// Définit une fonction à appeler pour signaler la progression
        /// d'une opération (plus ou moins) longue.
        /// </summary>
        /// <param name="percentage">
        /// Pourcentage de complétion de l'opération.
        /// </param>
        public delegate void ReportProgress(int percentage);


        /* === MÉTHODES PUBLIQUES (STATIQUES) === */

        /// <summary>
        /// Ecrit le contenu du volume voulu  directement sur le
        /// fichier image disque indiqué.
        /// <br/>
        /// Note : assurez-vous d'avoir suffisamment d'espace disponible
        /// sur le disque destination.
        /// </summary>
        /// <param name="volumeInfo">
        /// Volume source dont le contenu doit être recopié.
        /// </param>
        /// <param name="destFilePath">
        /// Chemin vers le fichier image disque destination à créer.
        /// Si ce fichier existe déjà, il sera écrase !
        /// </param>
        /// <param name="rpMethod">
        /// Méthode "callback" à appeler pour connaître la progression
        /// de l'opération d'écriture en temps réel.
        /// Passer <code>null</code> si l'on ne souhaite pas être notifié
        /// de cette progression.
        /// </param>
        public static void WriteVolumeIntoFile(DriveInfo volumeInfo,
                                               string destFilePath,
                                               ReportProgress rpMethod)
        {
            /* taille du volume / de la partition à recopier */
            long srcVolSize = volumeInfo.TotalSize;
            /* ouvre le volume source en lecture */
            string drivePath = String.Format(@"\\.\{0}:",
                                             volumeInfo.Name[0]);
            SafeFileHandle hVolume = null;
            try {
                hVolume = CreateFile(drivePath,
                                     DesiredAccess.GenericRead,
                                     ShareMode.FileShareRead
                                     | ShareMode.FileShareWrite,
                                     (IntPtr)null,
                                     CreationDistribution.OpenExisting,
                                     FileFlagsAndAttributes.None,
                                     (IntPtr)null);
                if (hVolume.IsInvalid) {
                    int errNum = Marshal.GetLastWin32Error();
                    throw new ApplicationException(
                        String.Format("Cannot open volume \"{0}\" !",
                                      drivePath),
                        new Win32Exception(errNum));
                }
                /* verrouille le volume */
                LockVolume(hVolume);
                /* démonte le volume si nécessaire */
                if (VolumeIsMounted(hVolume)) {
                    DismountVolume(hVolume);
                }
                /* effectue la copie proprement dite */
                DumpDevice(hVolume,
                           destFilePath,
                           srcVolSize,
                           rpMethod);
            } finally {
                if ((hVolume != null) &&
                    !(hVolume.IsInvalid) &&
                    !(hVolume.IsClosed))
                {
                    UnlockVolume(hVolume);
                    hVolume.Close();   /* appelle l'API W32 CloseHandle() */
                }
            }
        }

        /// <summary>
        /// Trouve le numéro de disque physique correspondant au volume
        /// dont les informations sont passées en paramètre.
        /// <br/>
        /// ATTENTION : ne fonctionne que pour les volumes simples,
        /// contenus sur un unique disque physique (donc pas les
        /// volumes RAID, etc.) !
        /// </summary>
        /// <param name="volumeInfo">
        /// Informations système décrivant le volume pour lequel on veut
        /// retrouver le disque physique correspondant.
        /// </param>
        /// <returns>
        /// Le numéro système NT du disque physique contenant le volume.
        /// </returns>
        public static uint FindPhysicalDrive(DriveInfo volumeInfo) {
            string drivePath = String.Format(@"\\.\{0}:",
                                             volumeInfo.Name[0]);
            SafeFileHandle hVolume = null;
            try {
                hVolume = CreateFile(drivePath,
                                     DesiredAccess.NoAccess,
                                     ShareMode.FileShareRead
                                     | ShareMode.FileShareWrite,
                                     (IntPtr)null,
                                     CreationDistribution.OpenExisting,
                                     FileFlagsAndAttributes.None,
                                     (IntPtr)null);
                if (hVolume.IsInvalid) {
                    int errNum = Marshal.GetLastWin32Error();
                    throw new ApplicationException(
                        String.Format("Cannot open volume \"{0}\" !",
                                      drivePath),
                        new Win32Exception(errNum));
                }

                return GetDeviceNumber(hVolume);

            } finally {
                if ((hVolume != null) &&
                    !(hVolume.IsInvalid) &&
                    !(hVolume.IsClosed))
                {
                    hVolume.Close();   /* appelle l'API W32 CloseHandle() */
                }
            }
        }

        /// <summary>
        /// Donne la taille totale, en octets, du disque physique dont
        /// le numéro d'ordre est donné en paramètre.
        /// </summary>
        /// <param name="physDrvNum">
        /// Numéro système NT du disque physique dont on veut la taille.
        /// </param>
        /// <returns>
        /// Taille en octets du disque physique indiqué.
        /// </returns>
        public static long GetPhysicalDriveSize(uint physDrvNum) {
            string physicalDriveRef = String.Format(@"\\.\PhysicalDrive{0}",
                                                    physDrvNum);
            SafeFileHandle hDisk = null;
            try {
                hDisk = CreateFile(physicalDriveRef,
                                   DesiredAccess.GenericRead,
                                   ShareMode.FileShareRead
                                   | ShareMode.FileShareWrite,
                                   (IntPtr)null,
                                   CreationDistribution.OpenExisting,
                                   FileFlagsAndAttributes.None,
                                   (IntPtr)null);
                if (hDisk.IsInvalid) {
                    int errNum = Marshal.GetLastWin32Error();
                    throw new ApplicationException(
                        String.Format("Cannot open \"{0}\" !",
                                      physicalDriveRef),
                        new Win32Exception(errNum));
                }

                return GetLengthInfo(hDisk);

            } finally {
                if ((hDisk != null) &&
                    !(hDisk.IsInvalid) &&
                    !(hDisk.IsClosed))
                {
                    hDisk.Close();   /* appelle l'API W32 CloseHandle() */
                }
            }
        }

        /// <summary>
        /// Ecrit le contenu du disque physique voulu  directement sur le
        /// fichier image disque indiqué.
        /// <br/>
        /// Note : assurez-vous d'avoir suffisamment d'espace disponible
        /// sur le disque destination.
        /// </summary>
        /// <param name="physDrvNum">
        /// Numéro d'ordre du disque physique dont le contenu doit être recopié.
        /// </param>
        /// <param name="destFilePath">
        /// Chemin vers le fichier image disque destination à créer.
        /// Si ce fichier existe déjà, il sera écrase !
        /// </param>
        /// <param name="rpMethod">
        /// Méthode "callback" à appeler pour connaître la progression
        /// de l'opération d'écriture en temps réel.
        /// Passer <code>null</code> si l'on ne souhaite pas être notifié
        /// de cette progression.
        /// </param>
        public static void WritePhysicalDiskIntoFile(DriveInfo volumeInfo,
                                                     string destFilePath,
                                                     ReportProgress rpMethod)
        {
            /* ouvre le volume voulu pour le verrouiller et le démonter */
            string drivePath = String.Format(@"\\.\{0}:",
                                             volumeInfo.Name[0]);
            SafeFileHandle hVolume = null;
            try {
                hVolume = CreateFile(drivePath,
                                     DesiredAccess.GenericRead,
                                     ShareMode.FileShareRead
                                     | ShareMode.FileShareWrite,
                                     (IntPtr)null,
                                     CreationDistribution.OpenExisting,
                                     FileFlagsAndAttributes.None,
                                     (IntPtr)null);
                if (hVolume.IsInvalid) {
                    int errNum = Marshal.GetLastWin32Error();
                    throw new ApplicationException(
                        String.Format("Cannot open volume \"{0}\" !",
                                      drivePath),
                        new Win32Exception(errNum));
                }
                /* verrouille le volume */
                LockVolume(hVolume);
                /* démonte le volume si nécessaire */
                if (VolumeIsMounted(hVolume)) {
                    DismountVolume(hVolume);
                }

                /* retrouve le disque physique correspondant aux volumes */
                uint physDrvNum = GetDeviceNumber(hVolume);
                string physicalDriveRef = String.Format(@"\\.\PhysicalDrive{0}",
                                                        physDrvNum);
                SafeFileHandle hDisk = null;
                try {
                    hDisk = CreateFile(physicalDriveRef,
                                       DesiredAccess.GenericRead,
                                       ShareMode.FileShareRead
                                       | ShareMode.FileShareWrite,
                                       (IntPtr)null,
                                       CreationDistribution.OpenExisting,
                                       FileFlagsAndAttributes.None,
                                       (IntPtr)null);
                    if (hDisk.IsInvalid) {
                        int errNum = Marshal.GetLastWin32Error();
                        throw new ApplicationException(
                            String.Format("Cannot open \"{0}\" !",
                                          physicalDriveRef),
                            new Win32Exception(errNum));
                    }

                    /* effectue la copie proprement dite */
                    DumpDevice(hDisk,
                               destFilePath,
                               GetLengthInfo(hDisk),
                               rpMethod);

                } finally {
                    if ((hDisk != null) &&
                        !(hDisk.IsInvalid) &&
                        !(hDisk.IsClosed)) {
                        hDisk.Close();   /* appelle l'API W32 CloseHandle() */
                    }
                }

            } finally {
                if ((hVolume != null) &&
                    !(hVolume.IsInvalid) &&
                    !(hVolume.IsClosed)) {
                    UnlockVolume(hVolume);
                    hVolume.Close();   /* appelle l'API W32 CloseHandle() */
                }
            }
        }


        /* === DÉCLARATION DE FONCTION EXTERNES (API Win32) === */

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern SafeFileHandle CreateFile(
            string lpFileName,
            DesiredAccess dwDesiredAccess,
            ShareMode dwShareMode,
            IntPtr lpSecurityAttributes,
            CreationDistribution dwCreationDistribution,
            FileFlagsAndAttributes dwFlagsAndAttributes,
            IntPtr hTemplateFile);
        [Flags]
        enum DesiredAccess : uint
        {
            NoAccess = 0x00000000,
            /* generic rights (most common) */
            GenericRead = 0x80000000,
            GenericWrite = 0x40000000,
            GenericExecute = 0x20000000,
            GenericAll = 0x10000000,
            /* standard rights */
            Delete = 0x00010000,
            ReadControl = 0x00020000,
            WriteDac = 0x00040000,
            WriteOwner = 0x00080000,
            Synchronize = 0x00100000,
            StandardRightsAll = 0x001F0000,
            /* security */
            AccessSystemSecurity = 0x01000000
        }
        [Flags]
        enum ShareMode : uint
        {
            FileNoShare = 0x00000000,
            FileShareRead = 0x00000001,
            FileShareWrite = 0x00000002,
            FileShareDelete = 0x00000004
        }
        [Flags]
        enum CreationDistribution : uint
        {
            CreateNew = 1,
            CreateAlways = 2,
            OpenExisting = 3,
            OpenAlways = 4,
            TruncateExisting = 5
        }
        [Flags]
        enum FileFlagsAndAttributes : uint
        {
            None = 0x00000000,
            /* attributes */
            FileAttributeReadOnly = 0x00000001,
            FileAttributeHidden = 0x00000002,
            FileAttributeSystem = 0x00000004,
            FileAttributeArchive = 0x00000020,
            FileAttributeNormal = 0x00000080,
            FileAttributeTemporary = 0x00000100,
            FileAttributeOffline = 0x00001000,
            FileAttributeEncrypted = 0x00004000,
            /* flags */
            FileFlagOpenNoRecall = 0x00100000,
            FileFlagOpenReparsePoint = 0x00200000,
            FileFlagSessionAware = 0x00800000,
            FileFlagPosixSemantics = 0x01000000,
            FileFlagBackupSemantics = 0x02000000,
            FileFlagDeleteOnClose = 0x04000000,
            FileFlagSequentialScan = 0x08000000,
            FileFlagRandomAccess = 0x10000000,
            FileFlagNoBuffering = 0x20000000,
            FileFlagOverlapped = 0x40000000,
            FileFlagWriteThrough = 0x80000000
        }


        [DllImport("Kernel32.dll", SetLastError = true)]
        static extern unsafe bool DeviceIoControl(SafeFileHandle hFile,
                                                  IoControlCode dwIoControlCode,
                                                  void* lpInBuffer,
                                                  int nInBufferSize,
                                                  void* lpOutBuffer,
                                                  int nOutBufferSize,
                                                  out int lpBytesReturned,
                                                  IntPtr lpOverlapped);
        [Flags]
        enum IoControlCode : uint
        {
            IoctlDiskGetLengthInfo = 0x0007405C,
            FsctlLockVolume      = 0x00090018,
            FsctlUnlockVolume    = 0x0009001C,
            FsctlDismountVolume  = 0x00090020,
            FsctlIsVolumeMounted = 0x00090028,
            IoctlStorageCheckVerify     = 0x002D4800,
            IoctlStorageCheckVerify2    = 0x002D0800,
            IoctlStorageGetDeviceNumber = 0x002D1080,
        }
        [Flags]
        enum TDeviceType : uint
        {
            FileDeviceDisk        = 0x00000007,
            FileDeviceFileSystem  = 0x00000009,
            FileDeviceMassStorage = 0x0000002D,
        }
        struct StorageDeviceNumber
        {
            public TDeviceType DeviceType;
            public uint DeviceNumber;
            public uint PartitionNumber;
        }


        [DllImport("Kernel32.dll", SetLastError = true)]
        static extern unsafe bool ReadFile(SafeFileHandle hFile,
                                           byte* lpBuffer,
                                           int nNumberOfBytesToRead,
                                           out int lpNumberOfBytesRead,
                                           IntPtr lpOverlapped);

        [DllImport("Kernel32.dll", SetLastError = true)]
        static extern unsafe bool WriteFile(SafeFileHandle hFile,
                                            byte* lpBuffer,
                                            int nNumberOfBytesToWrite,
                                            out int lpNumberOfBytesWritten,
                                            IntPtr lpOverlapped);


        [DllImport("Kernel32.dll", SetLastError = true)]
        static extern bool SetFilePointerEx(SafeFileHandle hFile,
                                            long liDistanceToMove,
                                            out long lpNewFilePointer,
                                            MoveMethod dwMoveMethod);
        [Flags]
        enum MoveMethod : uint
        {
            FileBegin = 0,
            FileCurrent = 1,
            FileEnd = 2
        }

    }
}

