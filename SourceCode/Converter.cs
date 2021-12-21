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
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Reflection;
    using Microsoft.MetadirectoryServices;
    using Vigo.Bas.ManagementAgent.Log;

    // Name       : Converter
    // Description:
    // Contains helper functions for converting between PIFU (autoconverted from pifu xsd) Values (mostly when the conversion became Item*) and Human Readable Values
    // Contains definitions on howto convert from "XML" elments into FIMCS "format"
    // Contains Helper functions to determine if a value is defined and correct default values if not defined
    public static class Converter
    {
        /* How to delimit XML elements into "AVP" format */
        public const char delimiterElements = '.';
        public const char delimiterAttributes = ';';
        public const char delimiterNumbers = '#';
        public const char delimiterValues = ',';
        public const char delimiterIDValues = ':';
        public const char delimiterBelongs = '@';

        public const int orgLevelMax = 2;
        public static HashSet<roleRoletype> supportedRoleTypes = new HashSet<roleRoletype>();
        public static HashSet<Tuple<string, int>> supportedGroupTypes = new HashSet<Tuple<string,int>>();
        public static HashSet<Tuple<string, int>> supportedGroupMembershipTypes = new HashSet<Tuple<string, int>>();

        // Objective   : Determine if a object is a specifier
        // Output      : true if the name of the property ends with Specified and is of type boolean
        // Require     : prop != null && prop.DisplayName != null
        // Description : Determine if a object exist to support attributes not being specififed in the xml
        // Limitation  : Is fragile and depends on xsd.exe and naming conventions (is correct for the parsed xsd but may need to change in future)
        public static bool isSpecifier( PropertyDescriptor prop )
        {
            string name = prop.DisplayName;
            return ( ( prop.PropertyType == typeof( Boolean ) ) && name.EndsWith( "Specified" ) );
        }

        // Objective   : Determine if a certain element (property) is defined for an object
        // Output      : true if there is no specifier for the elment or the specifier is true
        // Require     : prop != null && prop.DisplayName != null && obj != null
        // Description : Determine if a certain element is specified in the xml
        // Limitation  : Is fragile and depends on xsd.exe and naming conventions (is correct for the parsed xsd but may need to change in future)
        public static bool isSpecified( PropertyDescriptor prop, object obj )
        {
            string name = prop.DisplayName;
            PropertyInfo pi = prop.ComponentType.GetProperty( name + "Specified" );
            object value = ( pi != null ) ? pi.GetValue( obj, null ) : null;

            return ( value != null ) ? ( bool )value : true;
        }

        // Objective   : Get the current active ID in the datasource (our system) for the object
        // Output      : Current ID
        // Require     : sources  != null && 1 active ID
        // Description : Get the current active ID for the object
        // Limitation  : Only supports 1 Active ID, will halt if error
        public static string getOurID( sourcedid[] sources, string datasource )
        {
            string retur = string.Empty;
            sourcedid[] IDs = Array.FindAll( sources,
                       delegate( sourcedid s )
                       {
                           return ( ourSourceID( s, datasource ) );
                       }
                       );
            if( IDs.Length == 1 )
                retur = createID( IDs[0] );
            return retur;
        }
        public static string getSSN(userid[] userIds)
        {
            string retur = string.Empty;
            foreach (var id in userIds)
            {
                if (id.useridtype== useridtype.personNIN)
                {
                            retur = id.Value;
                    break;                   
                }
            }
            return retur;
        }

        public static string getPersonCode(userid[] userIds)
        {
            string retur = string.Empty;
            foreach (var id in userIds)
            {
                switch (id.useridtype)
                {
                    case useridtype.studentID:
                        {
                            retur = id.Value;
                            break;
                        }

                    case useridtype.workforceID:
                        {
                            retur = id.Value;
                            break;
                        }
                    case useridtype.personFIN:
                        {
                            retur = id.Value;
                            break;
                        }
                }               
            }
            return retur;
        }
        public static string getPersonCodeAsSourcedid(string personCode, string source)
        {
            string retur = createID(personCode, source);
            return retur;
        }

        public static string getContactTeacherIdAsSourcedid(string contactgroupPifuId, bool UsePersonCode, Dictionary<string, string> SSNtoPersonCodeList)
        {
            string contactTeacher = string.Empty;
            string ssn = contactgroupPifuId.Split('_')[1];



            string organizationPostFix = contactgroupPifuId.Substring(contactgroupPifuId.LastIndexOf('@') + 1).Split('#')[0];

            string personId = string.Empty;

            if (UsePersonCode)
            {
                if (SSNtoPersonCodeList.ContainsKey(ssn))
                {
                    personId = SSNtoPersonCodeList[ssn];
                }
                else
                {
                    Logger.Log.InfoFormat("Contact teacher with ssn: '{0}' is missing from SSNtoPersonCode dictionary and can't be added to All members and Instructor group for group {1}", ssn, contactgroupPifuId);
                }
            }
            else
            {
                personId = ssn;
            }
            if (!string.IsNullOrEmpty(personId))
            {
                contactTeacher = $"{personId}@{organizationPostFix}";
            }
            return contactTeacher;
        }

        // Objective   : Get the active ID for the object in othersystems (!in datasource)
        // Output      : ID
        // Require     : sources  != null
        // Description : Get the current ID for the object in other systems
        public static HashSet<object> getOtherSystemsIDs( sourcedid[] sources, string datasource )
        {
            sourcedid[] IDs = Array.FindAll( sources,
                       delegate( sourcedid s )
                       {
                           return ( otherSystemsSourceID( s, datasource ) );
                       }
                       );
            HashSet<object> retur = new HashSet<object>();
            if( IDs != null )
                foreach( sourcedid s in IDs )
                    retur.Add( createID( s ) );
            return retur;
        }

        // Objective   : Helper function to create an ID
        // Output      : a ID
        // Require     : id != null
        private static string createID( string id, string source )
        {
            return id + delimiterBelongs + source;
        }

        // Objective   : Helper function to create an ID
        // Output      : a ID
        // Require     : id != null
        public static string createID( sourcedid s )
        {
            return createID( s.id.Value, s.source.Value );
        }

        // Objective   : Delegate for finding Our SystemID
        // Output      : true if this is a currentsourced and is our system
        // Require     : s  != null
        private static bool ourSourceID( sourcedid s, string datasource )
        {
            return ( currentSourcedID( s ) && ( s.source.Value == datasource ) );
        }

        // Objective   : Delegate for finding othersSystemID
        // Output      : true if this is a currentsourced and is not our system
        // Require     : s  != null
        private static bool otherSystemsSourceID( sourcedid s, string datasource )
        {
            return ( currentSourcedID( s ) && ( s.source.Value != datasource ) );
        }

        // Objective   : Helper function for finding a current sourcedid
        // Output      : true if the id is active (sourcedidSourcedidtype.New)
        // Require     : s  != null
        private static bool currentSourcedID( sourcedid s )
        {
            return ( s.sourcedidtypeSpecified ) ? ( s.sourcedidtype == sourcedidSourcedidtype.New ) : true;
        }

        // Objective   : Get the Modificationtype depending on the recstatus
        // Output      : Add/Update/Delete
        // Require     : rec = recstatus.Item1, recstatus.Item2, recstatus.Item3
        // Limitation  : Will halt if error
        public static ObjectModificationType getCSModificationType( recstatus rec )
        {
            ObjectModificationType retur = ObjectModificationType.None;
            switch( ConvertFromValue( rec ) )
            {
                case "Add": //Add
                    retur = ObjectModificationType.Add;
                    break;

                case "Update": //Update
                    retur = ObjectModificationType.Update;
                    break;

                case "Delete": //Delete
                    retur = ObjectModificationType.Delete;
                    break;

                default:
                    throw new Exception( "Error : Unhandled recstatus " + rec.ToString() );
            }
            return retur;
        }

        // Objective   : The ConvertFromValue* methods are helper functions for translating between PIFU Value to Human Readable format
        // Output      : Value taken form the xsd or PIFU standard, if error returns <string>Invalid
        // Description :
        // When the values should be used for testing or choice (in code) use the ConvertFromValue() functions
        // Output can be bool or string; if the PIFU value only can be two values and is "bool like" the output will be bool.
        // The ConvertFromValueToString always return a string representation (ie should always be used when "saving" the value in FIM-CS or wanting a string representation)
        public static bool ConvertFromValue( pifu_unique val )
        {
            return val == pifu_unique.Item1;
        }

        public static bool ConvertFromValue( pifu_primaryRelation val )
        {
            return val == pifu_primaryRelation.Item0;
        }

        public static bool ConvertFromValue( roleStatus val )
        {
            return val == roleStatus.Item1;
        }

        private static string ConvertFromValue( demographicsGender val )
        {
            switch( val )
            {
                case demographicsGender.Item0:
                    return "Unknown";

                case demographicsGender.Item1:
                    return "Female";

                case demographicsGender.Item2:
                    return "Male";

                default:
                    throw new Exception( "Error : invalid gender " + val.ToString() );
            }
        }

        private static string ConvertFromValue( telTeltype val )
        {
            switch( val )
            {
                case telTeltype.Item1:
                    return "Voice";

                case telTeltype.Item2:
                    return "Fax";

                case telTeltype.Item3:
                    return "Mobile";

                default:
                    throw new Exception( "Error : invalid teltype " + val.ToString() );
            }
        }

        private static string ConvertFromValue( relationshipRelation val )
        {
            switch( val )
            {
                case relationshipRelation.Item1:
                    return "Parent";

                case relationshipRelation.Item3:
                    return "Alias";

                default:
                    throw new Exception( "Error : invalid realationship " + val.ToString() );
            }
        }

        private static string ConvertFromValue( memberIdtype val )
        {
            switch( val )
            {
                case memberIdtype.Item1:
                    return "Person";

                default:
                    throw new Exception( "Error : invalid member type " + val.ToString() );
            }
        }

        private static string ConvertFromValue( valuesValuetype val )
        {
            switch( val )
            {
                case valuesValuetype.Item0:
                    return "List";

                case valuesValuetype.Item1:
                    return "Continoues";

                default:
                    throw new Exception( "Error : invalid value type " + val.ToString() );
            }
        }

        private static string ConvertFromValue( recstatus val )
        {
            switch( val )
            {
                case recstatus.Item1:
                    return "Add";

                case recstatus.Item2:
                    return "Update";

                case recstatus.Item3:
                    return "Delete";

                default:
                    throw new Exception( "Error : invalid recstatus " + val.ToString() );
            }
        }

        // These values are from the XSD, the Documentation only supports 01 – elev, 02 – lærer/pedansatt, 04 – andre medlemmer (kan utdypes med subrolle)
        private static string ConvertFromValue( roleRoletype val )
        {
            switch( val )
            {
                case roleRoletype.Item01:
                    return "Learner";

                case roleRoletype.Item02:
                    return "Instructor";

                case roleRoletype.Item03:
                    return "Content Developer";

                case roleRoletype.Item04:
                    return "Member";

                case roleRoletype.Item05:
                    return "Manager";

                case roleRoletype.Item06:
                    return "Mentor";

                case roleRoletype.Item07:
                    return "Administrator";

                case roleRoletype.Item08:
                    return "Teaching Assistant";

                default:
                    throw new Exception( "Error : invalid role type " + val.ToString() );
            }
        }

        public static roleRoletype ConvertToRoleTypeFromString( string val )
        {
            switch( val )
            {
                case "Learner":
                    return roleRoletype.Item01;

                case "Instructor":
                    return roleRoletype.Item02;

                case "Content Developer":
                    return roleRoletype.Item03;

                case "Member":
                    return roleRoletype.Item04;

                case "Manager":
                    return roleRoletype.Item05;

                case "Mentor":
                    return roleRoletype.Item06;

                case "Administrator":
                    return roleRoletype.Item07;

                case "Teaching Assistant":
                    return roleRoletype.Item08;

                default:
                    throw new Exception( "Error : invalid role type (from string) " + val );
            }
        }

        public static string ConvertFromPifuRolesToFeideRoles(string val)
        {
            switch (val.ToLower())
            {
                case "learner":
                    return "student";
                case "instructor":
                    return "faculty";
                case "member":
                    return "staff";
                    
                default:
                    throw new Exception("Error : invalid Pifu role for Feide " + val);
            }
        }

        public static Tuple<string ,int> ConvertFromStringToSchemeAndGroupType(string val )
        {
            switch( val.ToLower() )
            {
                case "skoleeier":
                    return Tuple.Create("pifu-ims-go-org", 1); 

                case "skole":
                    return Tuple.Create("pifu-ims-go-org", 2) ;

                case "basisgruppe":
                    return Tuple.Create("pifu-ims-go-grp", 1);

                case "undervisningsgruppe":
                    return Tuple.Create("pifu-ims-go-grp", 2);

                case "kontaktlærergruppe":
                    return Tuple.Create("pifu-ims-go-grp", 3);

                case "trinn":
                    return Tuple.Create("pifu-ims-go-grp", 4);

                case "utdanningsprogram":
                    return Tuple.Create("pifu-ims-go-grp", 5);

                case "programområde":
                    return Tuple.Create("pifu-ims-go-grp", 6);

                case "fag":
                    return Tuple.Create("pifu-ims-go-grp", 7);

                case "foresattegruppe":
                    return Tuple.Create("pifu-ims-go-grp", 8);

                case "språkopplæring":
                    return Tuple.Create("pifu-ims-go-grp", 9);

                default:
                    throw new Exception( "Error : invalid  grouptype (from string) " + val );
            }
        }


        public static string ConvertFromValueToString( object val )
        {
            if( val.GetType() == typeof( demographicsGender ) )
                return ConvertFromValue( ( demographicsGender )val );

            if( val.GetType() == typeof( telTeltype ) )
                return ConvertFromValue( ( telTeltype )val );

            if( val.GetType() == typeof( relationshipRelation ) )
                return ConvertFromValue( ( relationshipRelation )val );

            if( val.GetType() == typeof( memberIdtype ) )
                return ConvertFromValue( ( memberIdtype )val );

            if( val.GetType() == typeof( valuesValuetype ) )
                return ConvertFromValue( ( valuesValuetype )val );

            if( val.GetType() == typeof( recstatus ) )
                return ConvertFromValue( ( recstatus )val );

            if( val.GetType() == typeof( roleRoletype ) )
                return ConvertFromValue( ( roleRoletype )val );

            if( val.GetType() == typeof( pifu_unique ) )
                return ConvertFromValue( ( pifu_unique )val ).ToString();

            if( val.GetType() == typeof( pifu_primaryRelation ) )
                return ConvertFromValue( ( pifu_primaryRelation )val ).ToString();

            if( val.GetType() == typeof( roleStatus ) )
                return ConvertFromValue( ( roleStatus )val ).ToString();

            return val.ToString();
        }
    }
}