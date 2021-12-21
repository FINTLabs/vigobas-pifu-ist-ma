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

    // Name : PIFUOrgGroup
    // Description: Contains the complete XML <group> + additional/methods used during conversion
    public class PIFUOrgGroup : PIFUGroup
    {
        [Obsolete( "typeName is invalid for PIFUOrgGroup, please use typeNames instead.", true )]
        new public const string typeName = "";

        public static List<string> typeNames
        {
            get
            {
                List<string> retur = new List<string>();
                for( int i = 0; i < Converter.orgLevelMax; i++ )
                {
                    retur.Add(PIFUObject.orgLevelPrefix + i);
                }
                return retur;
            }
        }


        //public HashSet<object> otherOrgids { get; private set; }

        // Objective : Default Constructor Input : the type (defaults to group Description :
        // Used as a default constructor, mainly for later calling getSchema that gets the PIFU
        // Schema from the xsd
        public PIFUOrgGroup( int level )
            : base( orgLevelPrefix + level )
        {
            base.isOrg = true;
            base.isGrp = false;
            base.groupType = level;
        }

        // Objective   : Constructor
        // Input       : unique ID,
        //             : the orglevel
        //             : possible other IDs,
        //             : the corresponding PIFU xmlObject,
        //             : the schema (retrived from FIM),
        // Output      : Initalized = true;
        // Description : Initialized the object for later retriving the values for import into CS
        public PIFUOrgGroup( string id, int level, HashSet<object> otherids, group g, Schema s, List<string> attributesToCS, Dictionary<string,string> schoolIdToSchoolCode )
            : base( id, level, otherids, g, s, attributesToCS, schoolIdToSchoolCode, true, orgLevelPrefix + level )
        {
            base.isOrg = true;
            base.isGrp = false;
            base.groupType = level;

            var localId = id.Split(Converter.delimiterBelongs)[0];
            if (schoolIdToSchoolCode.ContainsKey(localId))
            {
                base.sasSchoolCode = schoolIdToSchoolCode[localId];
            }

            //otherOrgids = otherids;
        }

        //#region implemented_base_class_methods

        //protected override Dictionary<string, object> getAttributeValues()
        //{
        //    Dictionary<string, object> retur = base.getAttributeValues();

        //    return retur;
        //}

        //protected override HashSet<SchemaAttribute> getSchemaAttributes()
        //{
        //    HashSet<SchemaAttribute> attributes = base.getSchemaAttributes();

        //    ////Remove Attributes Members.*
        //    //foreach( roleRoletype r in Enum.GetValues( typeof( roleRoletype ) ) )
        //    //{
        //    //    string keyRoles = memberRolePrefix + Converter.ConvertFromValueToString( r );
        //    //    attributes.Remove( SchemaAttribute.CreateMultiValuedAttribute( keyRoles, AttributeType.Reference ) );
        //    //}

        //    return attributes;
        //}

        //#endregion implemented_base_class_methods

        //public IEnumerable<pifu_id> otherids { get; set; }
    }
}