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

using Microsoft.MetadirectoryServices;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NDS.FIM.Agents.ECMA2.Pifu
{
    // Name : PIFUMembershipGroup
    // Description: Contains the complete XML <group> + additional/methods used during conversion
    public class PIFUMembershipGroup : PIFUGroup
    {
        new public const string typeName = "membershipGroup";

        public string membershipComments { get; private set; }

        private HashSet<member> _members_ = new HashSet<member>();

        public HashSet<object> Members { get; private set; }

        public Dictionary<string, string> MemberSubRoles { get; private set; }

        public readonly roleRoletype RoleType;

        // Objective : Default Constructor Input : the type (defaults to group Description :
        // Used as a default constructor, mainly for later calling getSchema that gets the PIFU
        // Schema from the xsd
        public PIFUMembershipGroup()
            : base( typeName )
        {
        }

        // Objective   : Constructor
        // Input       : parent
        //             : the corresponding roleType
        //             : a description (used for displayname
        //             : the schema (the parent schema)
        // Output      : Initalized = true;
        // Description : Initialized the object for later retriving the values for import into CS
        public PIFUMembershipGroup( PIFUGroup p, roleRoletype rt, Schema s, List<string> attributesToCS )
            : base( p.ID + Converter.delimiterNumbers + Converter.ConvertFromValueToString( rt ), -1, null, ( group )p.xmlSource, s, attributesToCS, new Dictionary<string, string>(), false, typeName, p )
        {
            RoleType = rt;
            Members = new HashSet<object>();
            MemberSubRoles = new Dictionary<string, string>();
            membershipComments = null;
            base.MemberGroups = p.MemberGroups;
            base.MemberOf = p.MemberOf;
            base.sasSchoolCode = p.sasSchoolCode;
        }

        // Objective   : add a member to this group
        // Input       : member
        // Output      : Members += member id
        // Description : Alos intalises the xmlSource with the member
        public void AddMember( string memberID, string subrole )
        {
            // Part of NDS code,used in the "magic hack" that is commented out
            // _members_.Add( mID );
            // string memberID = Converter.createID( mID.sourcedid );
            Members.Add( memberID );          
            if (!string.IsNullOrWhiteSpace(subrole ) )
            {
                if (MemberSubRoles.ContainsKey(memberID ) )
                    MemberSubRoles[memberID] += Converter.delimiterValues + subrole;
                else
                    MemberSubRoles.Add(memberID, subrole );
            }

        }

        // Objective   : add a comment to this group
        // Input       : != null
        // Output      : membershipCommnets "=" comments
        // Require     : membershipCommnets == null
        public void AddComment( comments comments )
        {
            if( comments != null ) //hangslen
            {
                if( comments.lang != null )
                {
                    if( membershipComments != null )
                        membershipComments += Converter.delimiterAttributes;

                    membershipComments += "lang" + Converter.delimiterIDValues + comments.lang;
                }

                if( comments.Value != null )
                {
                    if( membershipComments != null )
                        membershipComments += Converter.delimiterAttributes;

                    membershipComments += "Value" + Converter.delimiterIDValues + comments.Value;
                }
            }
        }

        #region implemented_base_class_methods

        protected override HashSet<SchemaAttribute> GetSchemaAttributes()
        {
            HashSet<SchemaAttribute> attributes = base.GetSchemaAttributes();

            //Remove Attributes Members.*
            foreach( roleRoletype r in Enum.GetValues( typeof( roleRoletype ) ) )
            {
                string keyRoles = memberRolePrefix + Converter.ConvertFromValueToString( r );
                attributes.Remove( SchemaAttribute.CreateMultiValuedAttribute( keyRoles, AttributeType.Reference ) );
            }

            attributes.Add( SchemaAttribute.CreateMultiValuedAttribute( "Members", AttributeType.Reference ) );
            attributes.Add( SchemaAttribute.CreateMultiValuedAttribute( "MemberSubRoles", AttributeType.String ) );
            attributes.Add( SchemaAttribute.CreateSingleValuedAttribute( "RoleType", AttributeType.String ) );
            attributes.Add( SchemaAttribute.CreateSingleValuedAttribute( "membershipComments", AttributeType.String ) );

            attributes.Add( SchemaAttribute.CreateMultiValuedAttribute( Reflector.attributPrefix + "member", AttributeType.String ) );

            return attributes;
        }

        protected override Dictionary<string, object> GetAttributeValues()
        {
            Dictionary<string, object> retur = base.GetAttributeValues();

            retur.Add( "RoleType", Converter.ConvertFromValueToString( RoleType ) );
            retur.Add( "Members", Members );
            if ( membershipComments != null )
                retur.Add( "membershipComments", membershipComments );

            //remove our ourself form the list
            retur.Remove( memberGroupPrefix + Converter.ConvertFromValueToString( RoleType ) );

            HashSet<object> msList = new HashSet<object>();
            foreach( var ms in MemberSubRoles )
                msList.Add( ms.Key + Converter.delimiterIDValues + ms.Value );

            retur.Add( "MemberSubRoles", msList );

            //#region magic_hack

            ////Here we make magic and create the membership attributes
            //membership m = new membership();
            //m.member = _members_.ToArray();
            //Reflector R = new Reflector( m );

            //Dictionary<string, SchemaAttribute> pifuAttributes = new Dictionary<string, SchemaAttribute>();  //the pifu attributes we are interested in
            //pifuAttributes.Add( Reflector.attributPrefix + "member", SchemaAttribute.CreateMultiValuedAttribute( Reflector.attributPrefix + "member", AttributeType.String ) );

            //R.ResolveNameAndValue( ref retur, pifuAttributes );

            //#endregion magic_hack

            return retur;
        }

        #endregion implemented_base_class_methods
    }
}