namespace ResurectPhone.Infrastructure.Windows.NokiaN9;

internal sealed record N9TlsPackage(string Package, string Version, string Filename, string? OriginalSha256);

internal static class N9TlsManifest
{
    // Bundle 0.0.2 and original PR1.3 packages checked on 2026-09-26.
    public const string BundleUrl = "https://wunderwungiel.pl/MeeGo/files/TLS%201.2%20for%20N9.tar.gz";
    public const string BundleSha256 = "6881BD4E1B899D8FBF37407E234DC1479C81DB908D6A8381EDE46EFD298358A6";
    public static readonly N9TlsPackage[] Packages =
    [
        new("cryptsetup", "1.0.7-12+0m6", "cryptsetup_1.0.7-12+0m6_armel.deb", "c3637d1300a7bcba868e3d994a5e3bda0ec589d3501493555a67265a3e75054f"),
        new("aegis-certman-common-ca", "1.0.8+0m8", "aegis-certman-common-ca_1.0.8+0m8_all.deb", "59c1fee9634ac9c9ac1db42ac6d9717b99ac09a020535a8f9367a1adeb36f42c"),
        new("libqt4-network", "4.7.4~git20120327-0maemo1+0m8", "libqt4-network_4.7.4~git20120327-0maemo1+0m8_armel.deb", "0b8124253df5f61f701cefb16656e7682bd1720a2624ac86d7f7ec83c04ce80d"),
        new("libsasl2-modules", "2.1.23.dfsg1-meego4+0m6", "libsasl2-modules_2.1.23.dfsg1-meego4+0m6_armel.deb", "f0742756ccc1f9308da49cd8bd178b31d96d9670c7c36d80984e811d94329569"),
        new("libaccounts-glib-tools", "1.2-1+0m7", "libaccounts-glib-tools_1.2-1+0m7_armel.deb", null),
        new("aegis-crypto-tools", "1.1.5+0m8", "aegis-crypto-tools_1.1.5+0m8_armel.deb", "c8392d4e33fde7100c247acb6ea92c007fb7b10fa4e2a325b13e52c4534e515b"),
        new("libsignoncrypto-qt", "1.3-1+0m8", "libsignoncrypto-qt_1.3-1+0m8_armel.deb", "5a059695c7cb8903f6b8d374886b4e3b3154b29dea7872d40a7db627de507875"),
        new("aegis-certman-tools", "1.0.8+0m8", "aegis-certman-tools_1.0.8+0m8_armel.deb", "0c34073fc35b7e49c488e3483ce7e4a523452086a58686484b48591d838575ac"),
        new("libaegis-crypto1", "1.1.5+0m8", "libaegis-crypto1_1.1.5+0m8_armel.deb", "f72249465f03f40d3c54d8931e63c19b1cb802078ffa99be7cc94971aa94bde5"),
        new("libcryptsetup0", "1.0.7-12+0m6", "libcryptsetup0_1.0.7-12+0m6_armel.deb", "0afbe4bd011cb2bdc87d5422170460c5966d5fed6d0b4f30b0626e69bb15de00"),
        new("libaccounts-glib0", "1.2-1+0m7", "libaccounts-glib0_1.2-1+0m7_armel.deb", "5de5fa54e2d5abc80ec0d6b5d6f4469042a67fb6dc88e5b1d2f7a8ae9a797931"),
        new("libaegis-certman0", "1.0.8+0m8", "libaegis-certman0_1.0.8+0m8_armel.deb", "76f832b1fab20cf041046b37610738e6c3dab49dc4c41d874165ec725a2ff900"),
        new("libsasl2-2", "2.1.23.dfsg1-meego4+0m6", "libsasl2-2_2.1.23.dfsg1-meego4+0m6_armel.deb", "baf540e57a12b49b4b2562cf484a768299fd8c568fc1341c1e584e42bcbe36ff"),
        new("libcurl3", "7.21.0-1maemo4+0m6", "libcurl3_7.21.0-1maemo4+0m6_armel.deb", "a2a24f0fdb628c400744c6d961c934c76afcfb56714d0cd049ff4074a28d7767"),
        new("wunderw-perl-opt", "5.38.0-1", "wunderw-perl-opt_5.38.0-1_armel.deb", null),
        new("openssl-local", "1.0.2u", "openssl-local_1.0.2u_armel.deb", null),
    ];
}
