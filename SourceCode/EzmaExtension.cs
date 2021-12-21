// VIGOBAS Identity Management System 
//  Copyright (C) 2021  Vigo IKS 
//  
//  Documentation - visit https://vigobas.vigoiks.no/ 
//  
//  This program is free software: you can redistribute it and/or modify 
//  it under the terms of the GNU Affero General Public License as 
//  published by the Free Software Foundation, either version 3 of the 
//  License, or (at your option) any later version. 
//  
//  This program is distributed in the hope that it will be useful, 
//  but WITHOUT ANY WARRANTY, without even the implied warranty of 
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the 
//  GNU Affero General Public License for more details. 
//  
//  You should have received a copy of the GNU Affero General Public License 
//  along with this program.  If not, see https://www.gnu.org/licenses/.

namespace NDS.FIM.Agents.ECMA2.Pifu
{
    using Microsoft.MetadirectoryServices;
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.IO;
    using Newtonsoft.Json;
    using Vigo.Bas.ManagementAgent.Log;

    public class EzmaExtension :
        // IMAExtensible2CallExport,
        IMAExtensible2CallImport,
        // IMAExtensible2FileImport,
        //IMAExtensible2FileExport,
        //IMAExtensible2GetHierarchy,
        IMAExtensible2GetSchema,
        IMAExtensible2GetCapabilities,
        IMAExtensible2GetParameters
        //IMAExtensible2GetPartitions
    {
        #region UI_parameters

        //Ugly hack to debugStuff to file, can be switched on or off in the interface, during normal execution this will be disabled, should be fixed.
        private const string debugPath = "c:/logs/fim-ma-debug.txt";

        public int ExportDefaultPageSize { get; set; }

        public int ExportMaxPageSize { get; set; }

        //UI parameters for import/Export (what are good defaults)?
        public int ImportDefaultPageSize { get; private set; }

        public int ImportMaxPageSize { get; private set; }
        
        // UI config parameter strings.
        private struct Param
        {
            public const string DEBUG = "Enable Debug Output to " + debugPath;
            public const string SchemaVersion = "XML Schema Version";
            public const string FullExportPath = "Full Export File Path";
            public const string FullImportPath = "Full Import File Path";
            //public const string useCSAttributesJsonFile = "Use CS attributes from json file";
            public const string usePersonCode ="Use PersonCode as person-id";
            public const string contactPersonsToCS = "Import contactpersons to CS";
            public const string StrictValidation = "Enable Validation (validates before parsing)";
            public const string AllowEmptyGroups = "Import empty groups to CS";
            public static List<string> SupportedRoleTypes = new List<string>();
            public static List<string> SupportedGroupTypes = new List<string>();
            public static List<string> SupportedGroupMembershipTypes = new List<string>();
            // public const string SchoolID = "Limit groups to SchoolID('02<schoolnumber>')";
        };

        #endregion UI_parameters

        //in how big chunks data, in number of records, should be imported into FIM/exported to external system.  Set by the pagesize parameter
        private int batchSize = 0;

        //Used during import/export
        private Queue<CSEntryChange> dataSourceEntries; //entries in the datasource system "converted" to CSEntryChanges

        //Stopwath to measure the time it takes to complete a entire import/export run.
        private Stopwatch importExportSW;

        // Enum telling if it is a Full or Delta import/export
        private OperationType opType;

        // Constructor set UI parameters for import/Export (what are good defaults)?
        public EzmaExtension()
        {
            Globals.TestMode = false;  //disables the testmode, ie the agent will produce CSEntries instead of outputting to file
            Trace.AutoFlush = false;   //when writing "debug" to file do not autoflush if running the release version of the code
            Debug.AutoFlush = true;    //when running the debug version of the code write immeditaly to file.

            foreach( roleRoletype r in Enum.GetValues( typeof( roleRoletype ) ) )
                Param.SupportedRoleTypes.Add( Converter.ConvertFromValueToString( r ) );

            foreach (t_groupschematypevalue gt in Enum.GetValues(typeof(t_groupschematypevalue)))
                Param.SupportedGroupTypes.Add(Converter.ConvertFromValueToString(gt));

            foreach (t_groupschematypevalue gt in Enum.GetValues(typeof(t_groupschematypevalue)))
                Param.SupportedGroupMembershipTypes.Add("membershipgrupper " +Converter.ConvertFromValueToString(gt));


            //Configure Run Profile UI parameters for import/Export (what are good defaults)?
            ImportDefaultPageSize = 200; //orig 12
            ImportMaxPageSize = 10000;    //orig 50
            ExportDefaultPageSize = 10;
            ExportMaxPageSize = 20;
        }

        // Objective : Define what we support and how we support it
        // http://msdn.microsoft.com/en-us/library/windows/desktop/microsoft.metadirectoryservices.imaextensible2getcapabilities.capabilities(v=vs.100).aspx
        public MACapabilities Capabilities
        {
            get
            {
                MACapabilities myCapabilities = new MACapabilities();

                //Run Profiles
                myCapabilities.DeltaImport = false;
                myCapabilities.FullExport = true;

                //Connected directory and LDAP behavior
                myCapabilities.ObjectRename = false;
                myCapabilities.ObjectConfirmation = MAObjectConfirmation.Normal;
                myCapabilities.DeleteAddAsReplace = false;
                myCapabilities.DistinguishedNameStyle = MADistinguishedNameStyle.Generic; //None DN=ANCHOR, Generic both DN and Anchor exist
                myCapabilities.NoReferenceValuesInFirstExport = false;

                //Sync and MA behavior
                myCapabilities.ConcurrentOperation = true;
                myCapabilities.ExportType = MAExportType.ObjectReplace;
                myCapabilities.Normalizations = MANormalizations.None;

                return myCapabilities;
            }
        }

        #region UI_methods

        //Objective   : Write out some informational stuff and get input paramters
        //Description : is called when setting or reconfiguring the MA
        public IList<ConfigParameterDefinition> GetConfigParameters( KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page )
        {
            List<ConfigParameterDefinition> configParametersDefinitions = new List<ConfigParameterDefinition>();
            switch( page )
            {
                case ConfigParameterPage.Connectivity:
                    /* Retrives some informational stuff */
                    Dictionary<string, string> schemas = new XmlParser().GetSupportedXMLSchemas();

                    List<string> versions = new List<string>();

                    string schemaLabel = "Supported XML schemas (version and namespace):\n";
                      
                    foreach (var versionName in schemas.Keys)
                    {
                        versions.Add(versionName);
                        var targetNamespace = schemas[versionName];
                        schemaLabel += $"{versionName}   {targetNamespace}\n";                        
                    }

                    string[] supportedVersions = versions.ToArray();

                    System.Reflection.Assembly runtimeAssembly = System.Reflection.Assembly.GetExecutingAssembly();

                    Version version = runtimeAssembly.GetName().Version;
                    string company = ( ( System.Reflection.AssemblyCompanyAttribute )Attribute.GetCustomAttribute( runtimeAssembly, typeof( System.Reflection.AssemblyCompanyAttribute ), false ) ).Company;
                    string title = ( ( System.Reflection.AssemblyTitleAttribute )Attribute.GetCustomAttribute( runtimeAssembly, typeof( System.Reflection.AssemblyTitleAttribute ), false ) ).Title;

                    configParametersDefinitions.AddRange(new[]
                    {
                        // Display the informational stuff
                        ConfigParameterDefinition.CreateLabelParameter("Product   : " + title + " version " + version.Major +  "." + version.Minor + " build ("+ version.Build +"." + version.Revision+")" + "\nCompany : " + company),

                        ConfigParameterDefinition.CreateLabelParameter(schemaLabel),
                        ConfigParameterDefinition.CreateDividerParameter(),
                        //Here comes the input paramters

                        ConfigParameterDefinition.CreateDropDownParameter(Param.SchemaVersion, supportedVersions,  false, supportedVersions[0]),
                        ConfigParameterDefinition.CreateStringParameter(Param.FullImportPath, String.Empty, "Complete File Path Here"),
                        //ConfigParameterDefinition.CreateCheckBoxParameter(Param.useCSAttributesJsonFile, false),
                        ConfigParameterDefinition.CreateCheckBoxParameter(Param.contactPersonsToCS, false),
                        ConfigParameterDefinition.CreateCheckBoxParameter(Param.AllowEmptyGroups, false)
                        //ConfigParameterDefinition.CreateStringParameter(Param.FullExportPath, String.Empty, "c:/export.xml")
                    } );

                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(Param.usePersonCode, true));
                    //Should we debug ???

                    configParametersDefinitions.Add( ConfigParameterDefinition.CreateDividerParameter() );
                    try
                    {
                        if( File.Exists( debugPath ) )
                            File.Delete( debugPath );
                    }
                    catch( Exception ) { }

                    configParametersDefinitions.Add( ConfigParameterDefinition.CreateCheckBoxParameter( Param.StrictValidation, false ) );

                    break;

                case ConfigParameterPage.Global:

                    configParametersDefinitions.Add( ConfigParameterDefinition.CreateLabelParameter( "Create the following group types:" ) );

                    foreach (string groupType in Param.SupportedGroupTypes)
                    {
                        if (!(groupType.Equals("skoleeier") || groupType.Equals("skole")))
                        {
                            configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(groupType, false));
                        }
                    }

                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateDividerParameter());
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateLabelParameter("Create the following membership group types:"));
                    foreach (string groupMembershipType in Param.SupportedGroupMembershipTypes)
                        configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(groupMembershipType, false));
                    //For testing purposes
                    //configParametersDefinitions.Add(ConfigParameterDefinition.CreateDividerParameter());
                    //configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter(Param.SchoolID, String.Empty, "SchoolID"));


                    break;

                case ConfigParameterPage.RunStep:

                    //Paramters here show up in Configure Run Profile 
                    break;


                case ConfigParameterPage.Partition:

                    break;

            }

            return configParametersDefinitions;
        }

        // Objective : get the possible attributes of the MA Description : is called after selecting
        // dll and pressing refresh schema or whenever refresh schema is pressed
        //
        // GetSchema is mandatory to implement for a call-based Management Agent. The goal of this
        // method is to return a schema representation where all objects have associated attributes.
        // For each returned object a list of attributes should be returned. The attribute will be
        // associated with its type. Note that the attribute list is shared between the object types
        // so if an attribute name is reused between objects the attribute definition cannot be
        // different. An attribute can be designed to be an anchor attribute. Anchor attribute(s)
        // are used to uniquely identify an object (see topic Understanding identifiers for a
        // detailed description). Non-anchor attributes are either single-valued or multi-valued and
        // can be of type String, Integer, Reference, Boolean, and Binary.
        // http: //msdn.microsoft.com/en-us/library/windows/desktop/microsoft.metadirectoryservices.imaextensible2getschema.getschema(v=vs.100).aspx
        //       Example SchemaType personType = SchemaType.Create("Person", false);
        //
        // personType.Attributes.Add(SchemaAttribute.CreateAnchorAttribute("anchor-attribute",
        // AttributeType.String));
        // personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("name",
        // AttributeType.String));
        // personType.Attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("email",
        // AttributeType.String));
        // personType.Attributes.Add(SchemaAttribute.CreateMultiValuedAttribute("multiAttr", AttributeType.String));
        //
        // Schema schema = Schema.Create(); schema.Types.Add(personType);
        //
        // return schema;
        public Schema GetSchema( KeyedCollection<string, ConfigParameter> configParameters )
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();

            Logger.Log.Info(System.Environment.NewLine + "New build running");
            Logger.Log.Info( System.Environment.NewLine + "Entering GetSchema" );

            HashSet<roleRoletype> roleTypes = new HashSet<roleRoletype>();
            foreach( string role in Param.SupportedRoleTypes )
                if( configParameters.Contains( role ) && configParameters[role].Value == "1" )
                    roleTypes.Add( Converter.ConvertToRoleTypeFromString( role ) );

            var groupTypes = new HashSet<Tuple<string,int>>();
            
            foreach (string group in Param.SupportedGroupTypes)
                if (group.Equals("skoleeier") || group.Equals("skole") ||(configParameters.Contains(group) && configParameters[group].Value == "1"))
                    groupTypes.Add(Converter.ConvertFromStringToSchemeAndGroupType(group));

            groupTypes = ( groupTypes.Count > 0 ) ? groupTypes : null;           

            Schema schema = Schema.Create();
            foreach (SchemaType s in new XmlParser(groupTypes).GetXMLSchemaTypes())
            {
                Logger.Log.Info("Adding schema " + s.Name);
                schema.Types.Add(s);
            }

            Logger.Log.Info( "Leaving Getschema elapsed time = " + sw.Elapsed );
            return schema;
        }

        //Objective   : Check so paramters are valid
        //Description : is called after when pressing "next" after GetConfigParameters()
        public ParameterValidationResult ValidateConfigParameters( KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page )
        {
            foreach( ConfigParameter param in configParameters )
            {
                switch( param.Name )
                {
                    case Param.FullImportPath:
                        if( string.IsNullOrEmpty( param.Value ) )
                        {
                            return new ParameterValidationResult( ParameterValidationResultCode.Failure,
                                string.Format( "You must supply '{0}'.", param.Name ),
                                param.Name );
                        }
                        else
                        {
                            try
                            {
                                File.Open( param.Value, FileMode.Open, FileAccess.Read ).Dispose();
                            }
                            catch( Exception )
                            {
                                return new ParameterValidationResult( ParameterValidationResultCode.Failure,
                                string.Format( "'{0}' = '{1}'but '{1}' cant be opened for Reading.", param.Name, param.Value ),
                                param.Name );
                            }
                        }
                        break;

                    case Param.FullExportPath:
                        if( string.IsNullOrEmpty( param.Value ) )
                        {
                            return new ParameterValidationResult( ParameterValidationResultCode.Failure,
                                string.Format( "You must supply '{0}'.", param.Name ),
                                param.Name );
                        }
                        else
                        {
                            try
                            {
                                File.Open( param.Value, FileMode.Open, FileAccess.Write ).Dispose();
                            }
                            catch( Exception )
                            {
                                return new ParameterValidationResult( ParameterValidationResultCode.Failure,
                                string.Format( "'{0}' = '{1}'but '{1}' cant be opened for Writing.", param.Name, param.Value ),
                                param.Name );
                            }
                            try
                            {
                                File.Open( param.Value, FileMode.Open, FileAccess.Read ).Dispose();
                            }
                            catch( Exception )
                            {
                                return new ParameterValidationResult( ParameterValidationResultCode.Failure,
                                string.Format( "'{0}' = '{1}'but '{1}' cant be opened for Reading.", param.Name, param.Value ),
                                param.Name );
                            }
                        }

                        break;

                    //case Param.useCSAttributesJsonFile:
                    // 
                    // Validation can't be done here because the MAUtils.Mafolder cant be called from here and this path will vary depending on MA name
                    // Instead existence of the file is tested in the OpenImportConnection method where MAUtils is available
                    


                    default:
                        break;
                }
            }

            Logger.Log.Info( "Passed \"Parameter Validation\", for parameters:" );
            foreach( ConfigParameter param in configParameters )
                Logger.Log.Info( param.Name + "=" + param.Value );
            Trace.Flush();
            return new ParameterValidationResult( ParameterValidationResultCode.Success, null, null );
        }

        #endregion UI_methods

        #region IMPORT_methods

        // CloseImportConnection is used to tell the extensible MA the session is over and allow
        // customer code to clean up. It can be assumed that OpenImportConnection has been called successfully.
        // http: //msdn.microsoft.com/en-us/library/windows/desktop/microsoft.metadirectoryservices.imaextensible2callimport.closeimportconnection(v=vs.100).aspx
        public CloseImportConnectionResults CloseImportConnection( CloseImportConnectionRunStep importRunStepInfo )
        {
            dataSourceEntries = null; //mayeb can fool the garbage collector to claim this memeory a little faster
            Logger.Log.Info( "Leaving CloseImportConnection, the entire import took = " + importExportSW.Elapsed );
            Trace.Flush();
            importExportSW.Stop();
            return new CloseImportConnectionResults();
        }

        // Objective : Reads from importEntries into CS, is called directly after
        // OpenImportConnection() on the same object Description : respects batchSize but deos not
        // care about opType (capabilities only allows Full import)
        //
        // GetImportEntries is the workhorse of import. This method will read all data from the
        // connected system and translate the information to the object model used by the
        // Synchronization Service. There are three different contexts the method will be called in:
        // “Full Import”, “Delta Import”, and “Full Object Import”. The first two are created as run
        // profiles by the administrator. The last can only be invoked by the engine. If you
        // implement this method then you must support “Full Import”. A Full Import should be
        // designed so it will always return the data as if it was the first time the data has been
        // read from the connected directory, i.e. everything should be returned as new (Add)
        // objects. All objects must be returned and they should only be presented once to the engine.
        //
        // The number of objects which can be returned is defined on the run step as the page size.
        // The actual number of objects returned can be smaller but it cannot be more than this
        // limit. This method is called until the result collection’s MoreToImport is set to false
        // http: //msdn.microsoft.com/en-us/library/windows/desktop/microsoft.metadirectoryservices.imaextensible2callimport.getimportentries(v=vs.100).aspx
        public GetImportEntriesResults GetImportEntries( GetImportEntriesRunStep importRunStep )
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            GetImportEntriesResults importReturnInfo = new GetImportEntriesResults();
            Logger.Log.Debug( System.Environment.NewLine + "Entering GetImportEntries" + "having " + dataSourceEntries.Count + " importEntries  using " + batchSize + " as importPageSize" );

            List<CSEntryChange> CSentries = new List<CSEntryChange>();

            for( int i = 0; i < batchSize && dataSourceEntries.Count > 0; i++ )
                CSentries.Add( dataSourceEntries.Dequeue() );

            importReturnInfo.MoreToImport = ( dataSourceEntries.Count > 0 );
            importReturnInfo.CSEntries = CSentries;

            Logger.Log.Debug( "Leaving GetImportEntries at "+ DateTime.Now.ToString("hh:mm:ss tt") + " with " + dataSourceEntries.Count + " left to import, elapsed time = " + sw.Elapsed );
            return importReturnInfo;
        }

        // Objective : setup the import connection Description : In our case this means opening
        // validaing and parsing the xmlfile
        //
        // OpenImportConnection is used to configure the import session and is called once at the
        // beginning of import.
        // http: //msdn.microsoft.com/en-us/library/windows/desktop/microsoft.metadirectoryservices.imaextensible2callimport.openimportconnection(v=vs.100).aspx
        public OpenImportConnectionResults OpenImportConnection(KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenImportConnectionRunStep importRunStep)
        {
            //Measure Time for the entire ImportSession
            importExportSW = new Stopwatch();
            importExportSW.Start();

            //Measure Time for the this method
            Stopwatch sw = new Stopwatch();
            sw.Start();

            // The SAS will only supply these the three roles: Learner, Instructor and Member
            HashSet<roleRoletype> roleTypes = new HashSet<roleRoletype>();
            roleTypes.Add(Converter.ConvertToRoleTypeFromString("Learner"));
            roleTypes.Add(Converter.ConvertToRoleTypeFromString("Instructor"));
            roleTypes.Add(Converter.ConvertToRoleTypeFromString("Member"));

            var groupTypes = new HashSet<Tuple<string, int>>();
            foreach (string group in Param.SupportedGroupTypes)
                if (group.Equals("skoleeier") || group.Equals("skole") || (configParameters.Contains(group) && configParameters[group].Value == "1"))
                    groupTypes.Add(Converter.ConvertFromStringToSchemeAndGroupType(group));


            var groupMembershipTypes = new HashSet<Tuple<string, int>>();
            foreach (string membershipgroup in Param.SupportedGroupMembershipTypes)
                if (configParameters.Contains(membershipgroup) && configParameters[membershipgroup].Value == "1")
                {
                    var group = membershipgroup.Split(' ')[1];
                    groupMembershipTypes.Add(Converter.ConvertFromStringToSchemeAndGroupType(group));
                }

            //Used in generating eduPersonEntitlement group entries. OrgNo not available 
            Dictionary<string, string> vigoNoToOrgNo = new Dictionary<string, string>();

            //Save the current settings for this import need it in GetImportEntries
            //importrunstep parameter pageSize are set in the run profile
            batchSize = importRunStep.PageSize;
            opType = importRunStep.ImportType;

            Logger.Log.Info(System.Environment.NewLine + "Entering OpenImportConnectionResults");
            Trace.Flush();

            string importFilePath = configParameters[Param.FullImportPath].Value;

            var csAttributesToUse = new Dictionary<string, List<string>>();

            //if (configParameters[Param.useCSAttributesJsonFile].Value == "1")
            //{
                string csAttributesJsonFile = MAUtils.MAFolder + "\\CSAttributes.json";

                if (File.Exists(csAttributesJsonFile))
                {
                    csAttributesToUse = GetCSAttributesToUse(csAttributesJsonFile);
                }
                else
                {
                    Logger.Log.Info("Error : The file " + csAttributesJsonFile + " was not found");
                    Logger.Log.Info("This file must exist");
                    Trace.Flush();
                throw new ExtensibleExtensionException("Error: The file " + csAttributesJsonFile + " was not found");

            }
            //}

            bool contactPersonsToCS = (configParameters[Param.contactPersonsToCS].Value == "1") ? true : false;
            bool usePersonCode = (configParameters[Param.usePersonCode].Value == "1") ? true : false;
            bool allowEmptyGroups = (configParameters[Param.AllowEmptyGroups].Value == "1");

            string schemaVersion = configParameters[Param.SchemaVersion].Value;
                        
            XmlParser importPifu = new XmlParser(importFilePath, schemaVersion, contactPersonsToCS,allowEmptyGroups, usePersonCode, csAttributesToUse, types, groupTypes , groupMembershipTypes, roleTypes);

            if( configParameters[Param.StrictValidation].Value == "1" )
            {
                Logger.Log.Info( "Validating : " + importFilePath );
                Trace.Flush();
                string msg;
                bool validated = importPifu.DeepValidate( out msg );

                if( !validated )
                {
                    Logger.Log.Info( "Warning : The XML file did not validate due to " + System.Environment.NewLine + msg );
                    Trace.Flush();
                    throw new ExtensibleExtensionException( "Error : " + importFilePath + " did not validate due to " + msg );
                }

                Logger.Log.Info( "Validation took " + sw.Elapsed + " now parsing ..." );
                Trace.Flush();
            }
            else
            {
                Logger.Log.Info( "Warning : Validation disabled in config, now trying to parse ...." );
            }
            try
            {                
                dataSourceEntries = importPifu.ParseXml(); //storing the xml in an entry change.
            }
            catch( Exception e)
            {
                Logger.Log.Info( "Error : The parsing failed due to \"" + e.Message + "\" aborting");
                throw e;
            }
            
            Logger.Log.Info( "Leaving OpenImportConnectionResults at " + DateTime.Now.ToString("hh:mm:ss tt") + " elapsed time = " + sw.Elapsed );
            Trace.Flush();
            return new OpenImportConnectionResults();
        }

        #endregion IMPORT_methods

        #region EXPORT_methods

        //Called at the end of an export run to disconnect the connected directory and to release resources.
        //http://msdn.microsoft.com/en-us/library/microsoft.metadirectoryservices.imaextensible2callexport.closeexportconnection%28v=vs.100%29.aspx
        public void CloseExportConnection( CloseExportConnectionRunStep exportRunStep )
        {
            dataSourceEntries = null; //maybe can fool the garbage collector to claim this memeory a little faster

            // do some export code

            Logger.Log.Info( "Leaving CloseExportConnection, the entire import took = " + importExportSW.Elapsed );
            Trace.Flush();
            importExportSW.Stop();
            throw new NotImplementedException();
        }

        //Begins an export run.
        //http://msdn.microsoft.com/en-us/library/microsoft.metadirectoryservices.imaextensible2callexport.openexportconnection%28v=vs.100%29.aspx
        //
        public void OpenExportConnection( KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenExportConnectionRunStep exportRunStep )
        {
            //Measure Time for the entire ImportSession
            importExportSW = new Stopwatch();
            importExportSW.Start();

            //Measure Time for the this method
            Stopwatch sw = new Stopwatch();
            sw.Start();

            batchSize = exportRunStep.BatchSize;
            opType = exportRunStep.ExportType;

            Logger.Log.Info( "Entering OpenExportConnection Connection" );
            Trace.Flush();
            // Do some export code probaly read in the current values

            Logger.Log.Info( "Leaving OpenExportConnection, elapsed time = " + sw.Elapsed );
            Trace.Flush();
            throw new NotImplementedException();
        }

        // PutExportEntries is the workhorse for export.
        // http: //msdn.microsoft.com/en-us/library/microsoft.metadirectoryservices.imaextensible2callexport.putexportentries%28v=vs.100%29.aspx
        //       It will be called until there are no more objects to be exported to the connected
        //       directory. Objects are provided in batches as specified on the Run Step profile.
        //       There are two different export types “Export” and “Full Export”. If this method is
        //       implemented then “Export” is expected to be implemented and is always offered as a
        //       possible run step. If Full Export is supported then the associated setting in the
        //       MACapabilities method should be set. For “Export” the engine will provide delta
        //       changes since last run. For “Full Export” the engine will provide all objects and
        //       all attributes in the connector space regardless if they have changed or not. “Full
        //       Export” is used in cases where the target directory does not store state or suffers
        //       from “amnesia” for which a full import cannot successfully detect a change.
        public PutExportEntriesResults PutExportEntries( IList<CSEntryChange> csentries )
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            Logger.Log.Info( "Entering PutExportEntries" );
            Trace.Flush();

            /*foreach (CSEntryChange csentryChange in csentries)
            {
                //PifuEntry entry = new PifuEntry(csentryChange.ObjectType);
                Entry entry = new Entry(csentryChange.ObjectType);
                foreach (string attrib in csentryChange.ChangedAttributeNames)
                {
                    entry.attributes[attrib] = csentryChange.AttributeChanges[attrib].ValueChanges[0].Value.ToString();
                }

                #region Add

                if (csentryChange.ObjectModificationType == ObjectModificationType.Add)
                {
                    exportEntries.Enqueue(entry);
                }

                #endregion Add

                #region Delete_TODO

                if (csentryChange.ObjectModificationType == ObjectModificationType.Delete)
                {
                    //TODO
                }

                #endregion Delete_TODO

                #region Update_TODO

                if (csentryChange.ObjectModificationType == ObjectModificationType.Update)
                {
                    foreach (string attribName in csentryChange.ChangedAttributeNames)
                    {
                        if (csentryChange.AttributeChanges[attribName].ModificationType == AttributeModificationType.Add)
                        {
                            //TODO
                        }
                        else if (csentryChange.AttributeChanges[attribName].ModificationType == AttributeModificationType.Delete)
                        {
                            //TODO
                        }
                        else if (csentryChange.AttributeChanges[attribName].ModificationType == AttributeModificationType.Replace)
                        {
                            //TODO
                        }
                        else if (csentryChange.AttributeChanges[attribName].ModificationType == AttributeModificationType.Update)
                        {
                            //TODO
                        }
                    }
                }

                #endregion Update_TODO
            }

            PutExportEntriesResults exportEntriesResults = new PutExportEntriesResults();

            return exportEntriesResults;*/

            Logger.Log.Info( "Leaving PutExportEntries, elapsed time = " + sw.Elapsed );
            Trace.Flush();
            throw new NotImplementedException();
        }

        #endregion EXPORT_methods

        #region private_methods
        private Dictionary<string, List<string>> GetCSAttributesToUse(string csAttributesConfigFilePath)
        {
            var csAttributesToUse = new Dictionary<string, List<string>>();
            string jsonData = File.ReadAllText(csAttributesConfigFilePath);
            csAttributesToUse = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(jsonData);
            return csAttributesToUse;
        }
        #endregion
    }
}