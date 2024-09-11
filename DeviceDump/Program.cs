using System;
using System.Collections;
using System.IO;
using System.Reflection;


namespace DeviceDump
{
    class Program
    {
        /* === VARIABLES GLOBALES === */

        private static bool DumpPhysicalDisk = false;
        private static DriveInfo srcVolume = null;
        private static string destImageFile = null;


        /* === MÉTHODES UTILITAIRES (PRIVÉES) === */

        /* représentation textuelle d'une taille en octets
         * en utilisant le multiple SI le plus approprié */
        private static String FormatDriveSize(long drsz) {
            const double ONE_KILOBYTE = 1024.0;
            const double ONE_MEGABYTE = 1024.0 * ONE_KILOBYTE;
            const double ONE_GIGABYTE = 1024.0 * ONE_MEGABYTE;
            const double ONE_TERABYTE = 1024.0 * ONE_GIGABYTE;
            const double ONE_PETABYTE = 1024.0 * ONE_TERABYTE;
            const double ONE_EXABYTE = 1024.0 * ONE_PETABYTE;

            string suffix;
            double driveSize = (double)drsz;
            if (driveSize > ONE_EXABYTE) {
                driveSize /= ONE_EXABYTE;
                suffix = "Eo";
            } else if (driveSize > ONE_PETABYTE) {
                driveSize /= ONE_PETABYTE;
                suffix = "Po";
            } else if (driveSize > ONE_TERABYTE) {
                driveSize /= ONE_TERABYTE;
                suffix = "To";
            } else if (driveSize > ONE_GIGABYTE) {
                driveSize /= ONE_GIGABYTE;
                suffix = "Go";
            } else if (driveSize > ONE_MEGABYTE) {
                driveSize /= ONE_MEGABYTE;
                suffix = "Mo";
            } else if (driveSize > ONE_KILOBYTE) {
                driveSize /= ONE_KILOBYTE;
                suffix = "Ko";
            } else {
                suffix = "o";
            }

            return String.Format("{0:N2} {1}", driveSize, suffix);
        }

        /* affiche la liste des volumes amovibles présents */
        private static void ShowRemovableDrives(TextWriter twDest) {
            twDest.WriteLine("UNITÉS AMOVIBLES DISPONIBLES");
            twDest.WriteLine("____________________________");
            twDest.WriteLine();
            twDest.Flush();
            foreach (DriveInfo di in DriveInfo.GetDrives()) {
                if (di.DriveType == DriveType.Removable) {
                    twDest.Write("Volume {0}", di.Name);
                    if (!(di.VolumeLabel.Trim().Equals(String.Empty))) {
                        twDest.Write(" (\"{0}\")", di.VolumeLabel);
                    }
                    twDest.WriteLine();
                    twDest.WriteLine("- Format {0}.", di.DriveFormat);
                    twDest.WriteLine("- Prêt ? {0}.",
                                     (di.IsReady ? "oui" : "non"));
                    twDest.Write("- Taille : {0}",
                                 FormatDriveSize(di.TotalSize));
                    twDest.Write(" ; {0} disponibles.",
                                 FormatDriveSize(di.TotalFreeSpace));
                    twDest.WriteLine();
                    uint pdrvNum = Kernel32Func.FindPhysicalDrive(di);
                    long pdrvSz = Kernel32Func.GetPhysicalDriveSize(pdrvNum);
                    twDest.WriteLine("- Disque physique no. {0} (taille {1}).",
                                     pdrvNum, FormatDriveSize(pdrvSz));
                    twDest.WriteLine();
                    twDest.Flush();
                }
            }
        }

        /* affiche un message d'aide sur l'utilisation du programme */
        private static void ShowUsage(TextWriter twDest) {
            twDest.WriteLine();
            twDest.WriteLine("Usage : {0} [<options>] <unité> <fich_dest>",
                    Path.GetFileName(Assembly.GetExecutingAssembly().Location));
            twDest.WriteLine();
            twDest.WriteLine("avec :");
            twDest.WriteLine("- <unité> : Volume amovible dont on veut utiliser l'image,");
            twDest.WriteLine("            de A: à Z: selon les unités amovibles présentes.");
            twDest.WriteLine("- <fich_dest> : Chemin vers le fichier image où écrire");
            twDest.WriteLine("                l'image du contenu du volume source.");
            twDest.WriteLine("                ATTENTION : si ce fichier existe déjà,");
            twDest.WriteLine("                            il sera écrasé !");
            twDest.WriteLine("Option(s) possible(s) :");
            twDest.WriteLine(" -p : Recopie le contenu de tout le disque physique contenant");
            twDest.WriteLine("      le volume amovible indiqué.");
            twDest.WriteLine();
            twDest.WriteLine("Lancer ce programme sans paramètre affiche la liste des");
            twDest.WriteLine("unités amovibles disponibles sur le système.");
            twDest.WriteLine();
            twDest.Flush();
        }

        /* afficge sur la console les détails d'une exception */
        private static void ShowException(Exception exc) {
            Console.Error.WriteLine("Type : {0}", exc.GetType().Name);
            Console.Error.WriteLine("Message : \"{0}\"", exc.Message);
            Console.Error.WriteLine("Source : {0}", exc.Source);
            Console.Error.WriteLine("Pile d'appels :\n{0}",
                                       exc.StackTrace);
            if (exc.Data != null && (exc.Data.Count > 0)) {
                Console.Error.WriteLine("Données intégrées :");
                foreach (DictionaryEntry dent in exc.Data) {
                    Console.Error.WriteLine(" - {0} : {1}",
                                            dent.Key,
                                            dent.Value);
                }
            }
            Console.Error.WriteLine();
            Console.Error.Flush();
            if (exc.InnerException != null) {
                Console.Error.WriteLine(" ** EXCEPTION INTERNE :");
                ShowException(exc.InnerException);
            }
        }


        private static void ShowProgress(int percent) {
            Console.Out.Write("\b\b\b\b\b\b\b\b{0} %...", percent);
        }


        /* === POINT D'ENTRÉE DU PROGRAMME === */

        public static void Main(string[] args) {
            /* si aucun argument n'est fourni,
               liste les disques amovibles présents */
            if (args.Length < 1) {
                ShowRemovableDrives(Console.Out);
                Environment.Exit(0);
            }

            /* on ne traite que deux ou trois arguments
               (pas un seul, ni quatre ou plus) ! */
            if ((args.Length < 2) && (args.Length > 3)) {
                Console.Error.WriteLine(
                        "ERREUR : mauvais nombre de paramètres !");
                ShowUsage(Console.Error);
                Environment.Exit(1);
            }

            /* pour chaque argument donné */
            foreach (string arg in args) {
                /* s'agit-il d'une option ? */
                if ((arg[0] == '-') || (arg[0] == '/')) {
                    /* si oui, la prend en compte... */
                    switch (Char.ToLower(arg[1])) {
                    case 'p':
                        /* copier le contenu de tout le disque physique */
                        DumpPhysicalDisk = true;
                        break;
                    default:
                        Console.Error.WriteLine(String.Format(
                                "ERREUR : Option '{0}' inconnue !",
                                arg[1]));
                        ShowUsage(Console.Error);
                        Environment.Exit(2);
                        break;
                    }
                    /* ... et passe à l'argument suivant */
                    continue;
                }

                if (srcVolume == null) {
                    /* récupère la lettre désignant l'unité voulue */
                    char letter = Char.ToUpper(arg[0]);
                    if ((letter < 'A') || (letter > 'Z')) {
                        /* Erreur : pas un nom d'unité ! */
                        Console.Error.WriteLine(String.Format(
                                "ERREUR : mauvais nom d'unité (\"{0}\") !",
                                arg));
                        ShowUsage(Console.Error);
                        Environment.Exit(3);
                    }

                    /* recherche les infos sur le drive voulu */
                    string drvName = String.Format(@"{0}:\", letter);
                    bool found = false;
                    foreach (DriveInfo di in DriveInfo.GetDrives()) {
                        if (di.Name.Equals(drvName)) {
                            srcVolume = di;
                            found = true;
                            break;
                        }
                    }
                    if (!found) {
                        /* drive demandé non trouvé
                           => liste les unités présentes */
                        Console.Error.WriteLine(String.Format(
                                "ERREUR : unité {0} introuvable !\n",
                                drvName));
                        ShowRemovableDrives(Console.Error);
                        Environment.Exit(3);
                    }
                    /* argument suivant */
                    continue;
                }

                if (destImageFile == null) {
                    /* chemin vers le fichier image destination */
                    destImageFile = Path.GetFullPath(arg.Trim());
                    /* argument suivant */
                    continue;
                }
            }

            /* traite l'unité source indiquée */
            try {
                if (DumpPhysicalDisk) {
                    /* contenu de tout le disque physique */
                    uint pdrvNum = Kernel32Func.FindPhysicalDrive(srcVolume);
                    Console.Out.WriteLine(String.Format(
                            "Recopie du disque physique no. {0}" +
                            " dans le fichier image \"{1}\"...",
                            pdrvNum, destImageFile));
                    Console.Out.Flush();
                    Kernel32Func.WritePhysicalDiskIntoFile(srcVolume,
                                                           destImageFile,
                                                           ShowProgress);
                } else {
                    /* contenu du volume uniquement */
                    Console.Out.WriteLine(String.Format(
                            "Recopie de {0} dans le fichier image \"{1}\"...",
                            srcVolume, destImageFile));
                    Console.Out.Flush();
                    Kernel32Func.WriteVolumeIntoFile(srcVolume,
                                                     destImageFile,
                                                     ShowProgress);
                }
            } catch (Exception exc) {
                Console.Error.WriteLine(
                        "\n\n*** SURVENUE D'UNE EXCEPTION ***");
                ShowException(exc);
                Environment.Exit(-1);
            }

            /* travail terminé */
            Console.Out.WriteLine("\b\b\b\b\b\b\b\bCopie terminée.");
            Console.Out.WriteLine();
            Console.Out.Flush();
            Environment.ExitCode = 0;
        }

    }
}

