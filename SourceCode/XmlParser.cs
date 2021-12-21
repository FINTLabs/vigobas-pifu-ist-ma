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
    using System.Diagnostics;
    using System.IO;
    using System.Xml;
    using System.Xml.Schema;
    using System.Xml.Serialization;
    using System.Linq;
    using Vigo.Bas.ManagementAgent.Log;
    using System.Text;

    // Name       : XmlParser
    // Description: Parses and validates a PIFU xml file for use in PIFUMAExtension
    // Handle memberships
    public class XmlParser
    {
        private string _xmlURI = string.Empty;
        private string _schemaVersion = string.Empty;

        private XmlSchema schemaPifuIMS11 = new XmlSchema();
        private XmlSchema schemaPifuIMS12 = new XmlSchema();


        private XmlReaderSettings readerSettings = new XmlReaderSettings();

        //The schemas (returned from the SyncService) that we should use
        private Schema schemas = null;

        private Dictionary<string, List<string>> attributesForObjectTypesToCS;

        private bool usePersonCode = false;
        private bool _contactPersonsToCS = false;
        private bool _allowEmptyGroups = false;
        private bool supportExperimentalCompactTransfer = false;
        private string validationString = string.Empty;

        private int noOfMissingPersoncodesOnPerson;
        private int noOfContacts;

        //misc stopwatches

        //private Stopwatch importGroupsSW;
        //private Stopwatch importPersonsSW;

        //Should we enable experimental and possible buggy compact transfer, if false we will silently ignore "compact " errors in memberships
        public XmlParser(HashSet<Tuple<string, int>> groupTypes = null, HashSet<Tuple<string, int>> groupMembershipTypes = null, HashSet<roleRoletype> roleTypes = null, bool enablecompactTransfer = false )
        {
            supportExperimentalCompactTransfer = enablecompactTransfer;

            if (groupTypes == null)
            {
                foreach (t_groupschematypevalue gt in Enum.GetValues(typeof(t_groupschematypevalue)))
                {
                    Converter.supportedGroupTypes.Add(Converter.ConvertFromStringToSchemeAndGroupType(Converter.ConvertFromValueToString(gt)));
                }
            }
            else
            {
                Converter.supportedGroupTypes = groupTypes;
            }

            if (groupMembershipTypes == null)
            {
                foreach (t_groupschematypevalue gt in Enum.GetValues(typeof(t_groupschematypevalue)))
                {
                    Converter.supportedGroupMembershipTypes.Add(Converter.ConvertFromStringToSchemeAndGroupType(Converter.ConvertFromValueToString(gt)));
                }
            }
            else
            {
                Converter.supportedGroupMembershipTypes = groupMembershipTypes;
            }


            if (roleTypes == null)
            {
                foreach (roleRoletype r in Enum.GetValues(typeof(roleRoletype)))
                {
                    Converter.supportedRoleTypes.Add(r);
                }
            }       
            else
            {
                Converter.supportedRoleTypes = roleTypes;
            }
        }

        // Objective   : Constructor
        // Input       : <string> Path to XML file, Schema to use when parsing, bool enable expermential comapct transfer
        // Description : Initialize the Object to parse the input file validation.
        public XmlParser( string xmlURI, string schemaVersion, bool contactPersonsToCS, bool allowEmptyGroups, bool usePersonCodeAsId, Dictionary<string, List<string>> CSattributesForObjectTypes, Schema types, HashSet<Tuple<string,int>> groupTypes, HashSet<Tuple<string, int>> groupMembershipTypes, HashSet<roleRoletype> roleTypes,  bool enablecompactTransfer = false )
            : this( groupTypes, groupMembershipTypes, roleTypes, enablecompactTransfer )
        {
            _xmlURI = xmlURI;
            _schemaVersion = schemaVersion;
            attributesForObjectTypesToCS = CSattributesForObjectTypes;
            schemas = types;
            _contactPersonsToCS = contactPersonsToCS;
            _allowEmptyGroups = allowEmptyGroups;
            usePersonCode = usePersonCodeAsId;

            schemaPifuIMS11 = XmlSchema.Read(XmlReader.Create(new StringReader(NDS.FIM.Agents.ECMA2.Pifu.Properties.Resources.PIFU_IMS_SAS_1_1)), null);
            schemaPifuIMS12 = XmlSchema.Read(XmlReader.Create(new StringReader(NDS.FIM.Agents.ECMA2.Pifu.Properties.Resources.PIFU_IMS_SAS_1_2)), null);

            XmlSchema schemaToUse = new XmlSchema();

            var versionName11 = schemaPifuIMS11.Version;

            if (_schemaVersion == versionName11)
            {
                schemaToUse = schemaPifuIMS11;
            }
            else
            {
                schemaToUse = schemaPifuIMS12;
            }

            readerSettings.ValidationType = ValidationType.Schema;
            readerSettings.Schemas.Add(schemaToUse);

            readerSettings.IgnoreWhitespace = true;
            readerSettings.IgnoreComments = true;
            readerSettings.ValidationFlags |= XmlSchemaValidationFlags.ProcessInlineSchema;
            readerSettings.ValidationFlags |= XmlSchemaValidationFlags.ProcessIdentityConstraints;
            readerSettings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
            readerSettings.ValidationEventHandler += new ValidationEventHandler( ValidationCallBack );
        }

        //  Objective   : Validate the XML file
        //  Output      : Calls ValidationCallBack that set validation string (msg) if error, returns true if document validates
        //  Require     : XmlParse(string, Schema, bool) (Schema, bool can be uninitialized)
        //  Description : Validates all elements of the document
        //  Limitations : Reads the entire document, i.e. can be a significant overhead with large documents
        public bool DeepValidate( out string msg )
        {
            validationString = string.Empty;
            using (var xmlDocReader = XmlReader.Create(_xmlURI, readerSettings))
            {
                while (xmlDocReader.Read())
                    ;
            }
            msg = validationString;
            return ( validationString == string.Empty );
        }

        //  Objective   : Retrive which XSD version we support
        //  Output      : Sucess   : string[2] = {Namespace}{Version} of the PIFU standard we support
        //              : Fail     : string[2] = {string.Empty}{string.Empty}
        //  Description : Reads the first Namespace and version from the embedded XSD schema
        public Dictionary<string, string> GetSupportedXMLSchemas()
        {
            XmlSchemaSet XMLschemas = new XmlSchemaSet();

            XMLschemas.Add(null, XmlReader.Create(new StringReader(NDS.FIM.Agents.ECMA2.Pifu.Properties.Resources.PIFU_IMS_SAS_1_1)));
            XMLschemas.Add( null, XmlReader.Create( new StringReader( NDS.FIM.Agents.ECMA2.Pifu.Properties.Resources.PIFU_IMS_SAS_1_2 ) ) );
            XMLschemas.Compile();
            Dictionary <string, string> retur = new Dictionary<string, string>();

            foreach( XmlSchema s in XMLschemas.Schemas() )
            {
                if (!string.IsNullOrEmpty(s.Version))
                {
                    retur.Add(s.Version, s.TargetNamespace);
                }                
            }
            return retur;
        }

        //   Objective   : Export the complete XML Schema based on the XSD file
        //   Output      : Schema containing all entries
        //   Description : Create a list of SchemaTypes be used in GetSchema method
        public HashSet<SchemaType> GetXMLSchemaTypes()
        {
            HashSet<SchemaType> retur = new HashSet<SchemaType>();

            retur.Add( new PIFUPerson().GetSchema() );
            retur.Add( new PIFUMembershipGroup().GetSchema() );
            retur.Add( new PIFUGroup().GetSchema() );

            for( int i = 0; i < Converter.orgLevelMax; i++ )
            {
                retur.Add(new PIFUOrgGroup(i).GetSchema());
            }
            return retur;
        }

        // Objective   : Parse the XML to CSEntryChanges according to the schema
        // Output      : List<CSEntryChange>
        // Require     : (XmlParse(string, Schema) && deepValidate()
        // Description : Parses the entrire document and translates the xml to a Queue of CSEntryChanges
        // Limitations : Malformed XML will result in undefined behavior, sudden death and eternal pain
        // Only Full Import is supported at the moment, <properties><type> full
        // Kompakt transfer (where the groups persons etc is supposed to be already setup) only have experimental support
        public Queue<CSEntryChange> ParseXml()
        {
            Queue<CSEntryChange> Entries = new Queue<CSEntryChange>();
            
            if ( schemas == null )
            {
                throw new Exception("Error: No schema is set");
            }
            bool useOrg = false;
            foreach( string level in PIFUOrgGroup.typeNames )
            {
                if (schemas.Types.Contains(level))
                {
                    useOrg = true;
                }
            }
            bool useGroups = schemas.Types.Contains( PIFUGroup.typeName );
            bool useMemberships = schemas.Types.Contains( PIFUMembershipGroup.typeName );
            bool usePersons = schemas.Types.Contains( PIFUPerson.typeName );   //pifuperson and persomn is similar

            // If the user have not selected any of the types we shall not do anything
            if( !( usePersons || useGroups || useOrg || useMemberships ) )
                return Entries; //is empty

            bool usePIFUPersons = usePersons;   //pifuperson and persomn is similar
            bool usePIFUGroups = useOrg || useGroups || useMemberships;

            //Now we have atleast choosen one of the types, we therefore parse the XML and return the rootObject (= entire file) can be optimized maybe yes (read each object only when needed)
            string source;
            enterprise rootObject;
            propertiesType exportType;

            ReadRootObject(out rootObject, out source, out exportType );

            if( exportType != propertiesType.full )
                throw new Exception( "Error: Full import not possible enterprise.properties.type= " + rootObject.properties.type.ToString() );

            //We now have a full export and a enterprise object to work with

            List<string> attributesToCS = new List<string>();

            if( attributesForObjectTypesToCS.ContainsKey("person"))
            {
                attributesToCS = attributesForObjectTypesToCS["person"];
            }

            // Parsing persons already here to be able to member ids from ssn to personcode in the membership objects
            Dictionary<string, PIFUPerson> PIFUPersons = new Dictionary<string, PIFUPerson>();
            Dictionary<string, string> SSNtoPersonCode = new Dictionary<string, string>();
            
            ParsePersons(rootObject.person, source, attributesToCS, ref PIFUPersons, ref SSNtoPersonCode);
            Logger.Log.Debug(String.Format("{0} person objects are missing Personcode", noOfMissingPersoncodesOnPerson));

            // Handle groups, since PIFUPersons use PIFUGroups below I define it
            // in outer scope and initalize it to 0 elements
            Dictionary<string, PIFUGroup> PIFUGroups = new Dictionary<string, PIFUGroup>();
            Dictionary<string, Dictionary<string, PIFUGroup>> orgUnitPIFUGroups = new Dictionary<string, Dictionary<string, PIFUGroup>>();

            if ( usePIFUGroups && rootObject.group != null ) 
            {
                ParseGroups (rootObject.group, source, attributesForObjectTypesToCS, ref orgUnitPIFUGroups );

                if (rootObject.membership != null && (useMemberships || useOrg))
                {
                    // Create a dictionary for the membership nodes for more efficient handling of memberships
                    Dictionary<string, membership> MemberShips = new Dictionary<string, membership>();

                    ParseMemberships(rootObject.membership, ref MemberShips);

                    foreach (string orgUnit in orgUnitPIFUGroups.Keys)
                    {
                        HandleMemberships(MemberShips, SSNtoPersonCode,  source, orgUnit, ref orgUnitPIFUGroups);
                    }
                    foreach (string orgUnit in orgUnitPIFUGroups.Keys)
                    {
                        foreach (PIFUGroup g in orgUnitPIFUGroups[orgUnit].Values)
                        {
                            if (useOrg)
                            {
                                g.HandleOrganisationalStructure(orgUnit, ref orgUnitPIFUGroups);   //set the correct org and units on groups
                            }
                            
                            if (useMemberships)
                            {
                                var groupScheme = (g.isOrg) ? ("pifu-ims-go-org") : "pifu-ims-go-grp";
                                var groupType = (g.isOrg) ? (g.groupType +1 ) : g.groupType;
                                var groupSchemeAndType = Tuple.Create(groupScheme, groupType );

                                if (Converter.supportedGroupTypes.Contains(groupSchemeAndType) || groupType == -1)
                                {
                                    if (schemas.Types.Contains(g.objectType))
                                    {
                                        CSEntryChange entry = g.GetEntry();
                                        if (!Globals.TestMode)
                                        {
                                            if (g.isOrg || _allowEmptyGroups)
                                            {
                                                Logger.Log.Info(entry.ObjectModificationType + "ing <" + entry.ObjectType + "> with DN : " + entry.DN);
                                                Entries.Enqueue(entry);
                                            }
                                            else if(entry.AttributeChanges.Contains("MemberRole.Learner") ||
                                                     entry.AttributeChanges.Contains("MemberRole.Instructor") ||
                                                     entry.AttributeChanges.Contains("MemberRole.Member") ||
                                                     entry.AttributeChanges.Contains("Members")) 
                                            {
                                                Logger.Log.Info(entry.ObjectModificationType + "ing <" + entry.ObjectType + "> with DN : " + entry.DN);
                                                Entries.Enqueue(entry);
                                            }
                                            else
                                            {
                                               Logger.Log.Info("Group " + g.ID + " contains no members and will not be added to CS");
                                            }
                                        }
                                        else
                                        {
                                            Logger.Log.Info(g.action + "ing <" + g.objectType + "> with Anchor : " + g.ID);
                                        }
                                    }
                                }

                                //if (Converter.supportedGroupMembershipTypes.Contains(groupSchemeAndType))
                                //{
                                //    var memberRoleGroupsToRemove = new HashSet<roleRoletype>();
                                //    foreach (PIFUMembershipGroup m in g.MemberGroups.Values)
                                //    {                                        
                                //        // Only add membership groups with members to CS
                                //        if ((m.Members.Any(x=>false)))
                                //        {
                                //           Logger.Log.Info("Membership group "+ m.ID + " contains no members and will not be added to CS");
                                //            memberRoleGroupsToRemove.Add(m.RoleType);
                                //        }
                                //        else
                                //        {
                                //            if (Converter.supportedRoleTypes.Contains(m.RoleType))
                                //            {
                                //                CSEntryChange entry = m.GetEntry();
                                //                if (!Globals.TestMode)
                                //                {
                                //                   Logger.Log.Info(entry.ObjectModificationType + "ing <" + entry.ObjectType + "> with DN : " + entry.DN);
                                //                    Entries.Enqueue(entry);
                                //                }
                                //                else
                                //                {
                                //                    Logger.Log.Info(m.action + "ing <" + m.objectType + "> with Anchor : " + m.ID);
                                //                }
                                //            }   
                                //        }
                                //    }
                                //    // Remove reference to the empty membershipgroups from the originating group:
                                //    foreach (roleRoletype memberRole in memberRoleGroupsToRemove)
                                //    {
                                //        g.MemberGroups.Remove(memberRole);

                                //        // Remove reference to this group in the other membership groups
                                //        foreach (PIFUMembershipGroup m in g.MemberGroups.Values)
                                //        {
                                //            m.MemberGroups.Remove(memberRole);
                                //        }
                                //    }

                                //}
                            }

                        }
                    }
                }
                    
            }

            if( usePIFUPersons && rootObject.person != null )
            {
                // Two next lines are already executed prior to the creation of membership objects due to conversion of ID from SSN to PersonCode:
                //Dictionary<string, PIFUPerson> PIFUPersons = new Dictionary<string, PIFUPerson>();
                //ParsePersons( rootObject.person, source, ref PIFUPersons, ref SSN);

                foreach( PIFUPerson p in PIFUPersons.Values )
                {
                    p.HandleContactPersonRole();

                    List <string> orgUnits = new List <string>();
                    foreach (string key in orgUnitPIFUGroups.Keys)
                    {
                        string sID = key + Converter.delimiterBelongs + source;
                        foreach (var pm in orgUnitPIFUGroups[key][sID].MemberGroups.Values)
                            if (pm.Members.Contains(p.ID))
                            {
                                orgUnits.Add(key);
                            }
                    }
                    
                    foreach (string orgUnit in orgUnits)
                    {
                        p.HandleMembership(orgUnit, usePersonCode, SSNtoPersonCode, ref orgUnitPIFUGroups);  //if rootObject.groups == null PIFUGroups will not contain any elements so this is safe alos if memberships is not used the Memberships groups will not conatian any values
                    }
                    
                    CSEntryChange entry = p.GetEntry();
                    if( !Globals.TestMode)
                    {
                        var _primaryRole = p.GetPrimaryRole();
                        
                        if (_primaryRole != null)
                        {
                            if (_contactPersonsToCS)
                            {
                               Logger.Log.Info(entry.ObjectModificationType + "ing <" + entry.ObjectType + "> with DN : " + entry.DN);
                                Entries.Enqueue(entry);
                               Logger.Log.Info(entry.ObjectModificationType + "ing <" + entry.ObjectType + "> with DN : " + entry.DN + " OK");
                            }
                            else
                            if (!_primaryRole.Equals("contactperson"))
                            {
                               Logger.Log.Info(entry.ObjectModificationType + "ing <" + entry.ObjectType + "> with DN : " + entry.DN);
                                Entries.Enqueue(entry);
                               Logger.Log.Info(entry.ObjectModificationType + "ing <" + entry.ObjectType + "> with DN : " + entry.DN + " OK");
                            }
                            else
                            {
                                Logger.Log.Info(p.ID + "is a contactperson and not added to CS because import of contact persons are disabled on the MA");
                            }
                        }
                        else
                        {
                            Logger.Log.Info(p.ID + " is not a contact person and not member of any org unit(school). The person is not added to CS. This should be probably corrected in the SAS.");
                        }
                    }
                    else
                    {
                        Logger.Log.Info(p.action + "ing <" + p.objectType + "> with Anchor : " + p.ID);
                    }   
                }
            }
            string _loginfo = ( _contactPersonsToCS ) ? "" : "not ";
            Logger.Log.Info(String.Format("Discovered {0} no of contact persons. Import of contact persons are {1}enabled on the MA, and therefore {1}added to CS", noOfContacts, _loginfo));

            int oNR = 0;
            foreach( var o in Globals.nrObjects )
            {
                oNR += o.Value;
                Logger.Log.Info( "Created " + o.Value + " nr of <" + o.Key + "> objects" );
            }
            Logger.Log.Info( "For a total of " + oNR + " nr of objects " );
            Logger.Log.Info( "With a total of " + Globals.nrAttributes + " attributes" );
            return Entries;
        }

        #region private_methods
        // Objective   : 
        private void ParseMemberships(membership[] memberships, ref Dictionary<string, membership> MBS)
        {
            foreach (membership m in memberships)
            {
                if (m.sourcedid != null )
                {
                    string ID = m.sourcedid.id.Value + "@" + m.sourcedid.source.Value;
                    MBS.Add(ID, m);
                }
            }
        }

        // Objective   : Parse <enterprise><groups>
        // Output      : Dictionary<ID, PIFUGroup> PIFUGroups where ID is <sourcedid><id> where <sourcedid><source> == source
        // Require     : groups != null
        // Description : Create new PIFUGroup(s) and add them to a dictionary with ID as key for easier retrival later.
        // The correct schematype to use is based on the group type that is set later
        private void ParseGroups(group[] groups, string source, Dictionary<string, List<string>> attributesForObjectTypesToCS, ref Dictionary<string, Dictionary<string, PIFUGroup>> PIFUGroups)
        {
            var schoolIdToSchoolCode = new Dictionary<string, string>();
            foreach (group g in groups)
            {
                foreach (var gt in g.grouptype)
                {
                    if(gt.scheme.Equals(grouptypeScheme.pifuimsgogrp) && gt.typevalue.level =="1")
                    {
                        string schoolId = g.relationship[0].sourcedid.id.Value;
                        if (g.description.@long != null && !schoolIdToSchoolCode.Keys.Contains(schoolId))
                        {
                            string schoolCode = g.description.@long.Split(':')[0];
                            if (!String.IsNullOrEmpty(schoolCode))
                            {
                                schoolIdToSchoolCode.Add(schoolId, schoolCode);
                            }
                        }
                    }
                }
                    
            }

            foreach (group g in groups)
            {
                if (g.sourcedid != null)
                {
                    string ID = Converter.getOurID(g.sourcedid, source);
                    HashSet<object> otherIDs = (Converter.getOtherSystemsIDs(g.sourcedid, source));
                    if (string.IsNullOrWhiteSpace(ID))
                        throw new Exception("Zero or More than one sourceid that is valid");
                    if (g.grouptype == null)
                        throw new Exception("Error: The xmlSource or grouptype is null for group " + ID + " + bailing out");


                    foreach (var gt in g.grouptype)
                    {
                        PIFUGroup PG = null;
                        var attributesToCS = new List<string>();
                        int level = -1;
                        if (gt.typevalue == null || gt.typevalue.level == null || !int.TryParse(gt.typevalue.level, out level))
                            throw new Exception("Error: The typelevel is not parseable for group " + ID + " + bailing out");
                        switch (gt.scheme)
                        {
                            case grouptypeScheme.pifuimsgoorg:
                                {
                                    if (attributesForObjectTypesToCS.ContainsKey("orggroup"))
                                    {
                                        attributesToCS = attributesForObjectTypesToCS["orggroup"];
                                    }
                                    PG = new PIFUOrgGroup(ID, level - 1, otherIDs, g, schemas, attributesToCS, schoolIdToSchoolCode);
                                    Logger.Log.Info(string.Format("PIFU_GO_ORG created with ID '{0}'.", ID));
                                }
                                break;

                            case grouptypeScheme.pifuimsgogrp:
                                {
                                    if (attributesForObjectTypesToCS.ContainsKey("group"))
                                    {
                                        attributesToCS = attributesForObjectTypesToCS["group"];
                                    }
                                    PG = new PIFUGroup(ID, level, otherIDs, g, schemas, attributesToCS, schoolIdToSchoolCode, true);
                                    Logger.Log.Info(string.Format("PIFU_GO_GROUP created with ID '{0}'.", ID));
                                }
                                break;

                            default:
                                throw new Exception("Error, Unknown grouptype, bailing out");
                        }
                        if (PG == null)
                        {
                            throw new Exception("Error: we did not manage to create a group");
                        }                            
                        string vigoNumber = "";
                        Dictionary<string, PIFUGroup> dictGroup = new Dictionary<string, PIFUGroup>();
                        if (gt.scheme == grouptypeScheme.pifuimsgoorg)
                        {
                            vigoNumber = g.sourcedid[0].id.Value;
                            dictGroup.Add(ID, PG);
                            PIFUGroups.Add(vigoNumber, dictGroup);
                            foreach (var memberGroupKeyVal in PG.MemberGroups)
                            {
                                var memberGroup = memberGroupKeyVal.Value;
                                var memberGroupID = memberGroup.ID;
                                PIFUGroups[vigoNumber].Add(memberGroupID, memberGroup);
                            }
                        }
                        else
                        {
                            vigoNumber = g.relationship[0].sourcedid.id.Value;
                            if (PIFUGroups.Keys.Contains(vigoNumber))
                            {
                                dictGroup.Add(ID, PG);
                                PIFUGroups[vigoNumber].Add(ID, PG);
                            }
                            else
                            {
                                Logger.Log.Info(string.Format("Parent object of group '{0}' with id '{1}' is missing from the xml.", ID, vigoNumber));
                            }
                        }
                    }
                }
            }
        }

        // Objective   : Set which persons that belongs to which group (and their properties)
        // Output      : Dictionary<string, PIFUGroup> where each group has a popoluated MemberRole, FinalResults, InterimResults and Abscensce
        // Require     : memberships != null; parseGroups(rootObject.group, source, ref PIFUGroups);
        // Description : Finds which person a group is member of and put the values of this in MemberRole<role, personID>
        // Also finds if the person has some results (Final or Iterim) or abscensce and store thos alos on the group
        // Limitations : Only limited support for "kompakt"  transfer (experimental), see below
        //
        // Fråga till : Snorre Løvås , Teknisk leder , Senter for IKT i utdanningen, snorre.lovas@iktsenteret.no
        // Måste alla grupper och personer som är refererade till i filen finnas med i filen vid full export ?
        // ex.
        // I fil PIFU-IMS_SAS_eksempel_fravar_2.xml (och PIFU-IMS_SAS_eksempel_fravar_2_kompakt.xml)
        // Finns ett <membership> object med
        // <sourcedid>
        //      <source>mitt-sas@måne.kommune.no</source>
        //      <id>global_ID_org_17</id>
        // </sourcedid>
        // Men det finns I den filen ingen motsvarande <group> object  med samma <sourcedid>, är detta en valid PIFU då det är en full export (<properties><type> full)?
        // Svar:
        //      Full er litt tvetydig ja, og kanskje ikke en bra type for “kompakt” versjonene. Kompakt-filene forutsetter at alle grupper og identifikatorer er i mottakersystemet på forhånd (beskrevet i kommentar øverst).
        //      Så “full” her betyr at det er en full dump av det overføringen skal inneholde (karakterer og/eller fravær), en delta vil være bare deler av det.
        //      Dette må avtales utenfor selve overføringen.
        //      Det var et ønske om å kunne gjøre det så kompakt som mulig når man hadde strukturene/gruppene og bare skulle overføre karakterer/fravær, men jeg er ikke egentlig sikker på om jeg personlig egentlig vil anbefale å bruke den kompakte overføringenmetoden.
        //
        // experimentall lösning : Skapa en Grupp med minimalt med attribut som krävs? Men hur veta om den här gruppen är group, unit eller org?

        private void HandleMemberships(Dictionary<string, membership> memberships, Dictionary<string,string> SSNtoPersonCode, string source, string unit, ref Dictionary<string, Dictionary <string, PIFUGroup>> orgPGs)
        {
            var PGs = orgPGs[unit];
            foreach (string key in PGs.Keys)
            {
                if (memberships.ContainsKey(key))
                {
                    membership ms = memberships[key];
                    string groupSource = ms.sourcedid.source.Value;
                    string groupID = Converter.createID(ms.sourcedid);

                    if (groupSource != source)
                        Logger.Log.Info("Warning: handling groupmembership for othersource : " + groupSource);

                    if (!PGs.ContainsKey(groupID)) // This is a error or kompakt transfer ie not all groups is included in the transfer
                    {
                        Logger.Log.Info("Warning: No groups found for membership : " + groupID);
                        if (!supportExperimentalCompactTransfer)  //if we don't want to experiment, ie don't support kompakt transfer skip this membership
                        {
                            continue;
                        }
                        throw new NotImplementedException("We have not yet implemented support for compact transfer");
                        //workaround for "kompakt" transfer where not all groups is included in the transfer
                        //PIFUGroup placeholder = new PIFUGroup( groupID, null, new group(), schemas, true);
                        //PGs.Add( groupID, placeholder );
                    }
                    //To create membership groups only for orggroups, uncomment next line
                    //if (PGs[groupID].isOrg) 
                    PGs[groupID].HandleMembership(ms, usePersonCode, SSNtoPersonCode);                    
                }
            }
        }

        // Objective   : Parse <enterprise><person>
        // Output      : Dictionary<ID, PIFUPerson> PIFUPerson where ID is <sourcedid><id> where <sourcedid><source> == source
        // Require     : persons != null
        // Description : Create new PIFUPerson(s) and add them to a dictionary with ID as key for easier retrival later.
        private void ParsePersons( person[] persons, string source, List<string> attributesToCS, ref Dictionary<string, PIFUPerson> PIFUPersons, ref Dictionary<string, string> SSNToPersonCode)
        {
            var contactPersons = new HashSet<string>();
            string _contactPersonsToCSLoginfo = (_contactPersonsToCS) ? "" : "not ";
            foreach (person p in persons)
            {
                if (p.extension != null && p.extension.pifu_hasContactPerson != null)
                {
                    foreach (var contactperson in p?.extension?.pifu_hasContactPerson)
                    {
                        var contactpersonID = Converter.createID(contactperson.sourcedid);
                        if (!contactPersons.Contains(contactpersonID))
                        {
                            contactPersons.Add(contactpersonID);
                           Logger.Log.Debug(String.Format("Person object with ID '{0}' is a contactperson for student with id {1}", contactpersonID, Converter.createID(p.sourcedid[0])));
                            noOfContacts++;
                        }
                    }
                }
            }

            foreach ( person p in persons )
            {
                var personID = Converter.createID(p.sourcedid[0]);
                var isContact = (contactPersons.Contains(personID)) ? true : false;

                string ID = string.Empty;

                if (usePersonCode)
                {
                    string personCode = Converter.getPersonCode(p.userid);
                    string ssn = Converter.getSSN(p.userid);
                    if (string.IsNullOrEmpty(personCode))
                    {
                        if (isContact)                        
                        {
                            //A contact person who is missing personcode, is probably just a contact and ok to add to CS without personcode( if contact persons should be added to CS)
                            ID = Converter.getOurID(p.sourcedid, source);
                           Logger.Log.Debug(String.Format("Contact person with ssn: '{0}'  is missing personcode value, {1} added to CS", ssn, _contactPersonsToCSLoginfo));
                        }
                        else
                        {
                           Logger.Log.Debug(String.Format("Student/employee person with ssn: '{0}'  is missing personcode value and not added to CS", ssn));
                            noOfMissingPersoncodesOnPerson++;

                        }
                        continue;
                    }
                    else
                    {
                        if (SSNToPersonCode.ContainsValue(personCode))
                        {
                            var key = SSNToPersonCode.FirstOrDefault(x => x.Value == personCode).Key;
                           Logger.Log.Debug(String.Format("Persons with ssn '{0}' and '{1}' have the same personcode: {2}'", ssn, key, personCode));
                            continue;
                        }
                        else
                        {
                            SSNToPersonCode[ssn] = personCode;

                            ID = Converter.getPersonCodeAsSourcedid(personCode, source);

                        }
                    }
                }
                else
                {
                    ID = Converter.getOurID(p.sourcedid, source);
                }
                HashSet<object> otherIDs = (Converter.getOtherSystemsIDs(p.sourcedid, source));

                if (string.IsNullOrWhiteSpace(ID))
                {
                    throw new Exception("Zero or More than one sourceid that is valid");
                }
                //Logger.Log.Debug(String.Format("Adding Person object with ID '{0}' to PIFUPersons dictionary", ID));
                PIFUPerson PP = new PIFUPerson(ID, otherIDs, p, schemas, attributesToCS, isContact);
                PIFUPersons.Add(ID, PP);
            }
        }

        // Objective   : Parse <enterprise>
        // Output      : enterprise, source and exportType where source = <properties><datasource> and exportType  = <properties><type>
        // Require     : persons != null
        // Description : reads the enterprise object and returns current datasource, exportType and the rootObject(enterprise)
        // Limitation  : Parses the entire document, i.e. can be a significant overhead with large documents, will halt if no properties on rootObject
        private void ReadRootObject( out enterprise rootObject, out string source, out propertiesType exportType )
        {
            var xmlFileToParse = string.Empty;

            schemaPifuIMS11 = XmlSchema.Read(XmlReader.Create(new StringReader(NDS.FIM.Agents.ECMA2.Pifu.Properties.Resources.PIFU_IMS_SAS_1_1)), null);
            schemaPifuIMS12 = XmlSchema.Read(XmlReader.Create(new StringReader(NDS.FIM.Agents.ECMA2.Pifu.Properties.Resources.PIFU_IMS_SAS_1_2)), null);

            var versionName11 = schemaPifuIMS11.Version;

            if (_schemaVersion == versionName11)
            {
                Logger.Log.InfoFormat("ReadRootObject: Input xml has Pifu IMS version '{0}'. Replacing namespace in xml started", versionName11);

                var targetNameSpaceVer11 = schemaPifuIMS11.TargetNamespace;
                var targetNameSpaceVer12 = schemaPifuIMS12.TargetNamespace;

                readerSettings.Schemas.Remove(schemaPifuIMS11);
                readerSettings.Schemas.Add(schemaPifuIMS12);

                var tmpFile = CreateTmpFile();
                ReplaceTextInFile(_xmlURI, tmpFile, targetNameSpaceVer11, targetNameSpaceVer12);

                xmlFileToParse = tmpFile;
                Logger.Log.Info("ReadRootObject: Replacing namespace in xml ended");
            }
            else
            {
                Logger.Log.InfoFormat("ReadRootObject: Input xml has har latest Pifu IMS version ({0}). Replacing namespace in xml not necessary", schemaPifuIMS12.Version);
                xmlFileToParse = _xmlURI;
            }

            using (XmlReader xmlDocReader = XmlReader.Create(xmlFileToParse, readerSettings))
            {

                if (xmlDocReader.ReadToDescendant("enterprise"))
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(enterprise));

                    Logger.Log.Info("ReadRootObject: Deserializing xmlReader object started");
                    rootObject = (enterprise)serializer.Deserialize(xmlDocReader);
                    Logger.Log.Info("ReadRootObject: Deserializing xmlReader object ended");

                    if (rootObject == null)
                    {
                        throw new Exception("Error: rootObject == null");
                    }
                    if (rootObject.properties == null)
                    {
                        throw new Exception("Error: No properties on rootObject");
                    }
                    exportType = rootObject.properties.type;
                    source = rootObject.properties.datasource.Value;                  
                }
                else
                {
                    throw new Exception("Error: No rootObject \"enterprise\"");
                }
            }
            if (_schemaVersion == versionName11)
            {
                DeleteTmpFile(xmlFileToParse);
            }
            // Finished reading XML return rootObject
            return;
        }

        // Objective   : Xml Validation Event Handler
        // Input       : <object> sender, <ValidationEventArgs> what has caused the Event
        // Output      : validationString is uppdated (Both warning and fail)
        // Description : Is called when the (read) validation fails of the XmlFile
        private void ValidationCallBack( object sender, ValidationEventArgs args )
        {
            if( args.Severity == XmlSeverityType.Warning )
            {
                validationString += "Warning: Matching schema not found.  No validation occurred." + args.Message + System.Environment.NewLine;
            }   
            else
            {
                validationString += "Error: Validation error on line : " + args.Exception.LineNumber + " position : " + args.Exception.LinePosition + ". msg : " + args.Message + System.Environment.NewLine;
            }   
        }

        private static void ReplaceTextInFile(string originalFile, string outputFile, string searchTerm, string replaceTerm)
        {
            string tempLineValue;
            using (FileStream inputStream = File.OpenRead(originalFile))
            {
                using (StreamReader inputReader = new StreamReader(inputStream))
                {
                    using (StreamWriter outputWriter = File.AppendText(outputFile))
                    {
                        while (null != (tempLineValue = inputReader.ReadLine()))
                        {
                            outputWriter.WriteLine(tempLineValue.Replace(searchTerm, replaceTerm));
                        }
                    }
                }
            }
        }
        private static string CreateTmpFile()
        {
            string fileName = string.Empty;

            try
            {
                fileName = Path.GetTempFileName();

                FileInfo fileInfo = new FileInfo(fileName);

                fileInfo.Attributes = FileAttributes.Temporary;

                Logger.Log.DebugFormat("TEMP file created at: {0}", fileName);
            }
            catch (Exception ex)
            {
                Logger.Log.ErrorFormat("Unable to create TEMP file or set its attributes: {0}", ex.Message);
            }
            return fileName;
        }
        private static void DeleteTmpFile(string tmpFile)
        {
            try
            {
                if (File.Exists(tmpFile))
                {
                    File.Delete(tmpFile);
                    Logger.Log.Debug("TEMP file deleted.");
                }
            }
            catch (Exception ex)
            {
                Logger.Log.ErrorFormat("Error deleteing TEMP file: {0}", ex.Message);
            }
        }

        #endregion private_methods
    }
}