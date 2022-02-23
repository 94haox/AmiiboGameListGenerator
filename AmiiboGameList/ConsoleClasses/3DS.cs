﻿namespace AmiiboGameList
{
    /// <summary>Class containing an array of 3DS games</summary>
    [System.Serializable()]
    [System.ComponentModel.DesignerCategory("code")]
    [System.Xml.Serialization.XmlRoot("releases")]
    public class DSreleases
    {
        [System.Xml.Serialization.XmlElement("release")]
        public DSreleasesRelease[] release;
    }

    /// <summary>Class for each 3DS game.</summary>
    [System.Serializable()]
    [System.ComponentModel.DesignerCategory("code")]
    [System.Xml.Serialization.XmlType(AnonymousType = true)]
    public class DSreleasesRelease
    {
        public string name;
        public string titleid;
    }
}
