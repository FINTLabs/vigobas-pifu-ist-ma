namespace TestDriver
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using Microsoft.MetadirectoryServices;
    using NDS.FIM.Agents.ECMA2;
    using NDS.FIM.Agents.ECMA2.Pifu;

    internal class Program
    {
        private static void Main( string[] args )
        {
            Globals.TestMode = true;

            string file = "PIFU-IMS_SAS_eksempel.xml";
            //string file = "PIFU-IMS_SAS_eksempel_fravar_1.xml";
            //string file = "PIFU-IMS_SAS_eksempel_fravar_1_kompakt.xml";
            //string file = "PIFU-IMS_SAS_eksempel_fravar_2.xml";
            //string file = "PIFU-IMS_SAS_eksempel_fravar_2_kompakt.xml";
            //string file = "PIFU-IMS_SAS_eksempel_karakter_1.xml";
            //string file = "PIFU-IMS_SAS_eksempel_karakter_1_kompakt.xml";
            //string file = "PIFU-IMS_SAS_eksempel_karakter_2.xml";
            //string file = "PIFU-IMS_SAS_eksempel_karakter_2_kompakt.xml";
  

            try
            {
                File.Open( "Schemas.txt", FileMode.Create, FileAccess.Write ).Dispose();
                File.Open( "Values.txt", FileMode.Create, FileAccess.Write ).Dispose();
            }
            catch( IOException )
            {
                throw new Exception("Could not open save files" );
            }
            TextWriterTraceListener schemafile = new TextWriterTraceListener( "Schemas.txt" );
            TextWriterTraceListener valuesfile = new TextWriterTraceListener( "Values.txt" );

            Stopwatch sw = new Stopwatch();

            sw.Start();

            Trace.Listeners.Add( schemafile );
            System.Reflection.Assembly runtimeAssembly = System.Reflection.Assembly.GetExecutingAssembly();
            Version version = runtimeAssembly.GetName().Version;
            string company = ( ( System.Reflection.AssemblyCompanyAttribute )Attribute.GetCustomAttribute( runtimeAssembly, typeof( System.Reflection.AssemblyCompanyAttribute ), false ) ).Company;
            string title = ( ( System.Reflection.AssemblyTitleAttribute )Attribute.GetCustomAttribute( runtimeAssembly, typeof( System.Reflection.AssemblyTitleAttribute ), false ) ).Title;
            Trace.WriteLine( version.Major + "." + version.Minor + " Build " + version.Build + "." + version.Revision );
            Trace.WriteLine( version );
            Trace.WriteLine( new XmlParser().GetSupportedXMLSchema()[ 0 ] );
            HashSet<Microsoft.MetadirectoryServices.SchemaType> schemaEntries = new XmlParser().GetXMLSchemaTypes();
            Schema s = new Schema();
            foreach( var e in schemaEntries )
                s.Types.Add( e );
            sw.Stop();
            Trace.WriteLine( "Elapsed=" + sw.Elapsed );
            Trace.Flush();
            Trace.Listeners.Remove( schemafile );

            sw.Reset();

           
                sw.Start();

                Trace.Listeners.Add( valuesfile );
                Trace.WriteLine( "Using file : " + file );

                XmlParser pifu = new XmlParser( "docs/exempel/" + file, s, null, null, false );
                string msg  = "Validating file";
                //pifu.deepValidate( out msg );
                Trace.Write( msg );
                Trace.Flush();

                Queue<CSEntryChange> Entries = pifu.ParseXml();
                sw.Stop();
                Trace.WriteLine( "Elapsed=" + sw.Elapsed );

                Trace.Flush();

                Trace.Listeners.Remove( valuesfile );
        }
    }
}