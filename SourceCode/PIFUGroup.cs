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
    using System.ComponentModel;
    using System.Diagnostics;
    using Vigo.Bas.ManagementAgent.Log;

    // Name : PIFUGroup
    // Description: Similar to the XML <group> object
    public class PIFUGroup : PIFUObject
    {
        public const string typeName = "group";

        public HashSet<object> grepCode { get; private set; }
        public HashSet<object> allMembers { get; private set; }

        // to be used by PIFUPerson creating eduPersonEntitlement group values as mandatory in Feide 1.6
        public string organizationNumber { get; private set; }

        public string grepCodeShortForm { get; private set; }

        public string localGroupID { get; private set; }

        public string startDateTime { get; private set; }

        public string endDateTime { get; private set; }

        public string groupFriendlyName { get; private set; }

        //set by handleOrganisation method
        public bool isOrg { get; protected set; }

        public bool isGrp { get; protected set; }

        public int groupType { get; protected set; }

        public string groupTypeAsString { get; protected set; }

        public string sasSchoolCode { get; protected set; }

        public bool PersonCodeInUse { get; protected set; }

        public Dictionary<string, string> SSNtoPersonCodeList { get; protected set; }



        /*This is exported to FIM-CS*/

        public Dictionary<roleRoletype, PIFUMembershipGroup> MemberGroups { get; protected set; }

        public readonly string memberGroupPrefix = "MemberGroup" + Converter.delimiterElements;
        public static readonly string memberRolePrefix = "MemberRole" + Converter.delimiterElements;
        protected group xmlGroup; //the same as xmlSource

        // Objective : Default Constructor Input : the type (defaults to group Description :
        // Used as a default constructor, mainly for later calling getSchema that gets the PIFU
        // Schema from the xsd
        public PIFUGroup( string t = typeName )
            : base( t, new group() )
        {
        }

        // Objective   : Constructor
        // Input       : unique ID, possible other IDs or null,
        //             : the corresponding PIFU xmlObject, the schema (retrived from FIM),
        //             : should we create membership groups (used to avoid infinite loops when comes to membership groups),
        //             : the CS object type, deafults to "group",
        //             : do we have known parent ?
        // Output      : Initalized = true;
        // Description : Initialized the object for later retriving the values for import into CS
        public PIFUGroup(string id, int level, HashSet<object> otherids, group g, Schema s, List<string> attributesToCS, Dictionary<string, string> schoolIdToSchoolCode, bool createMembershipGroups, string t = typeName, PIFUGroup parent = null)
            : base(id, otherids, t, g, s, attributesToCS, ((g.recstatusSpecified) ? Converter.getCSModificationType(g.recstatus) : Converter.getCSModificationType(recstatus.Item1)))
        {
            isOrg = false;
            isGrp = true;
            groupType = level;
            xmlGroup = g;
            MemberGroups = new Dictionary<roleRoletype, PIFUMembershipGroup>();
            grepCode = new HashSet<object>();

            if (parent != null)
                base.ParentGroups.Add(parent.ID);
            else if (xmlGroup.relationship != null)
                HandleRealtionships(xmlGroup.relationship); //set the parents

            if (xmlGroup.extension != null && xmlGroup.extension.pifu_id != null)          //grepCodes
                foreach (pifu_id pid in xmlGroup.extension.pifu_id)
                {
                    if (pid.typeSpecified && pid.type == pifu_idType.grepCode)
                        grepCode.Add(pid.pifu_value);
                    if (pid.typeSpecified && pid.type == pifu_idType.grepCodeShortForm)
                        grepCodeShortForm = pid.pifu_value;
                }


            if (xmlGroup.description != null)
            {
                if (xmlGroup.description.@short != null)
                {
                    localGroupID = xmlGroup.description.@short.ToString();
                }
                if (xmlGroup.description.@long != null)
                {
                    groupFriendlyName = xmlGroup.description.@long.ToString();
                }
            }           

            if (xmlGroup.timeframe != null)
            {
                if (xmlGroup.timeframe.begin != null)
                {
                    startDateTime = xmlGroup.timeframe.begin.Value.ToString("yyyy-MM-dd");
                }
                if (xmlGroup.timeframe.end != null)
                {
                    endDateTime = xmlGroup.timeframe.end.Value.ToString("yyyy-MM-dd");
                }
            }

            if (xmlGroup.grouptype != null)
            {
                var gt = xmlGroup.grouptype[0];
                if (gt.scheme.Equals(grouptypeScheme.pifuimsgogrp))
                {
                    if (xmlGroup.relationship != null)
                    {
                        string parentGroupID = xmlGroup.relationship[0].sourcedid.id.Value.ToString();    
                        if (schoolIdToSchoolCode.ContainsKey(parentGroupID))
                        {
                            sasSchoolCode = schoolIdToSchoolCode[parentGroupID];
                        }
                    }
                }
            }

            //the membershipgroups, no members yet
            //only create membership groups for orglevel groups:  && t.ToLower().Contains("orglevel")
            // adding && t != null && t.ToLower().Contains("orglevel" generates an error:
            // The parsing failed due to "The given key was not present in the dictionary." aborting
            if (createMembershipGroups )
            {
                foreach (roleRoletype rt in Converter.supportedRoleTypes)
                {
                    PIFUMembershipGroup PM = new PIFUMembershipGroup(this, rt, s, attributesToCS);
                    MemberGroups.Add(rt, PM);
                }
            }
        }

        // Objective : Find the parents for this group Require : PIFUGroup( string id,
        // HashSet<object> otherids, group g, Schema s, bool createMembershipGroups, string t =
        // type, PIFUGroup parent = null ) Limitations : Only handles parent type of
        // realtionships not alias , will fail if unhandled realtionship and silently ignore
        // alias realtionship
        public void HandleOrganisationalStructure(string key, ref  Dictionary<string,Dictionary<string, PIFUGroup>> OUPG)
        {
            FindOrg(base.ParentGroups, ref OUPG);
        }

        // Objective : assign members to the group and set their roles, results and abscences as appropriate
        // Input : the members of this group
        // Output : Members are set for the group in the correct membershipgroup
        // Description : Membership Add/Update are treated identical, Delete are silently ignored
        public void HandleMembership( membership ms, bool usePersonCode, Dictionary<string, string> SSNtoPersonCode)
        {
            PersonCodeInUse = usePersonCode;
            SSNtoPersonCodeList = SSNtoPersonCode;
            foreach ( member m in ms.member )
            {                
                string memberID = String.Empty;
                if (usePersonCode)
                {
                    string ssn = m.sourcedid.id.Value;
                    if (SSNtoPersonCode.ContainsKey(ssn))
                    {
                        string personCode = SSNtoPersonCode[ssn];
                        string memberSource = m.sourcedid.source.Value;
                        memberID = Converter.getPersonCodeAsSourcedid(personCode, memberSource);
                    }
                    else
                    {
                        string groupID = ms.sourcedid.id.Value + Converter.delimiterBelongs + ms.sourcedid.source.Value;
                        Logger.Log.DebugFormat("Member with ssn: '{0}'  is missing from SSNtoPersonCode dictionary and will not be added to group '{1}'", ssn, groupID);
                        continue;
                    }
                } 
                else
                {                    
                    memberID = Converter.createID(m.sourcedid);
                }

                switch (Converter.ConvertFromValueToString(m.idtype))
                    {
                        case "Person": //person is the only valid in this version of PIFU
                            foreach (role r in m.role)
                            {
                                if (Converter.ConvertFromValue(r.status)) // Active and of correct roletype
                                {
                                    if (!r.roletypeSpecified)
                                        throw new Exception("Error: no RoleType is specified for memberID " + memberID + ", bailing out");
                                    recstatus rec = (r.recstatusSpecified) ? r.recstatus : recstatus.Item1;

                                    switch (Converter.ConvertFromValueToString(rec))
                                    {
                                        case "Add":
                                        case "Update":
                                            MemberGroups[r.roletype].AddMember(memberID, r.subrole);
                                            if (ms.comments != null && MemberGroups[r.roletype].membershipComments == null)
                                                MemberGroups[r.roletype].AddComment(ms.comments);
                                            break;
                                    case "Delete":
                                            Debug.Assert(false, "Warning : we do not yet explicitly handle membership delete, in prodcution we will ignore this membership and continue");
                                            break;
                                    }
                                }
                            }
                            break;
                    }

            }
        }

        #region implemented_base_class_methods

        protected override Dictionary<string, object> GetAttributeValues()
        {
            Dictionary<string, object> retur = new Dictionary<string, object>();
                       
            //Org and GroupType
            retur.Add( "groupType", groupType.ToString());    
            allMembers = new HashSet<object>();   
            

            //Members.
            foreach ( var mg in MemberGroups )
            {
                string keyRoles = memberRolePrefix + Converter.ConvertFromValueToString( mg.Key );
                if (mg.Value.ID.StartsWith("3_"))
                {
                    try
                    {
                        string contactgroupPifuIdMemberGroup = mg.Value.ID;

                        string contactTeacher = Converter.getContactTeacherIdAsSourcedid(contactgroupPifuIdMemberGroup, PersonCodeInUse, SSNtoPersonCodeList);

                        if (!string.IsNullOrEmpty(contactTeacher))
                        {
                            if (!allMembers.Contains(contactTeacher))
                            {
                                allMembers.Add(contactTeacher);
                                string contactgroupPifuId = contactgroupPifuIdMemberGroup.Split('#')[0];
                                Logger.Log.InfoFormat("Contact teacher: '{0}' is added to All members group for contactgroup: {1}", contactTeacher, contactgroupPifuId);
                            }
                            if (mg.Value.ID.EndsWith("Instructor"))
                            {
                                mg.Value.Members.Add(contactTeacher);
                                Logger.Log.InfoFormat("Contact teacher: '{0}' is added to group {1}", contactTeacher, contactgroupPifuIdMemberGroup);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        var message = e.Message;
                        throw new Exception($"Adding contact teacher to allMembers/Instructor group failed with: exception {message}");
                    }
                }
                retur.Add( keyRoles, mg.Value.Members );
                allMembers.UnionWith(mg.Value.Members);
            }
            
            foreach( var mg in MemberGroups )
            {
                if( Converter.supportedRoleTypes.Contains( mg.Key ) )// need to this to avoid referencing groups that did get exprted to FIM
                {
                    string key = memberGroupPrefix + Converter.ConvertFromValueToString( mg.Key );
                    retur.Add( key, mg.Value.ID );
                }
            }
            retur.Add("MemberRole.All", allMembers);

            //populate shortcut attributes
            if ( xmlGroup.extension != null )
            {
                if( xmlGroup.extension.pifu_tel != null )
                    foreach( var a in base.HandlePifu_tel( xmlGroup.extension.pifu_tel ) )
                        retur.Add( a.Key, a.Value );

                if( xmlGroup.extension.pifu_id != null )
                    foreach( var a in base.HandlePifu_id( xmlGroup.extension.pifu_id ) )
                        retur.Add( a.Key, a.Value );

                if( xmlGroup.extension.pifu_adr != null )
                    foreach( var a in base.HandlePifu_adr( xmlGroup.extension.pifu_adr ) )
                        retur.Add( a.Key, a.Value );

                if (xmlGroup.extension.pifu_email != null)
                    foreach (var a in base.handlePifu_email(xmlGroup.extension.pifu_email))
                        retur.Add(a.Key, a.Value);
            }

            // get grouptype
            if (xmlGroup.grouptype != null)
            {
                foreach (grouptype gt in xmlGroup.grouptype)
                {
                    groupTypeAsString = gt.typevalue.Value.ToString();
                    retur.Add("groupTypeAsString", groupTypeAsString);
                }
            }

            if (xmlGroup.relationship != null)
            {
                retur.Add("schoolName", xmlGroup.relationship[0].label);
            }
                    
            if (sasSchoolCode != null)
            {
                retur.Add("schoolCode", sasSchoolCode);
            }   
                 
            return retur;
        }
        

        protected override HashSet<SchemaAttribute> GetSchemaAttributes()
        {
            HashSet<SchemaAttribute> attributes = new HashSet<SchemaAttribute>();

            attributes.Add( SchemaAttribute.CreateSingleValuedAttribute( "groupType", AttributeType.String ) );
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("groupTypeAsString", AttributeType.String));
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("schoolName", AttributeType.String));
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("schoolCode", AttributeType.String));
            attributes.Add(SchemaAttribute.CreateMultiValuedAttribute("MemberRole.All", AttributeType.Reference));

            //MemberGroups and Members.
            foreach ( roleRoletype r in Converter.supportedRoleTypes )
            {
                string key = memberGroupPrefix + Converter.ConvertFromValueToString( r );
                string keyRoles = memberRolePrefix + Converter.ConvertFromValueToString( r );
                attributes.Add( SchemaAttribute.CreateSingleValuedAttribute( key, AttributeType.Reference ) );
                attributes.Add( SchemaAttribute.CreateMultiValuedAttribute( keyRoles, AttributeType.Reference ) );
            }

            //pifu_id
            foreach( string name in Enum.GetNames( typeof( pifu_idType ) ) )
            {
                attributes.Add( SchemaAttribute.CreateSingleValuedAttribute( name, AttributeType.String ) );
            }

            //pifu_adr
            foreach( string name in Enum.GetNames( typeof( pifu_adrType ) ) )
            {
                foreach( PropertyDescriptor prop in TypeDescriptor.GetProperties( typeof( adr ) ) )
                {
                    string curName = prop.DisplayName;
                    string key = name + Converter.delimiterElements + curName;

                    if( !Converter.isSpecifier( prop ) )
                    {
                        attributes.Add( SchemaAttribute.CreateSingleValuedAttribute( key, AttributeType.String ) );
                    }
                }
            }

            //pifu_tel
            foreach( string name in Enum.GetNames( typeof( pifu_telType ) ) )
            {
                attributes.Add( SchemaAttribute.CreateSingleValuedAttribute( name, AttributeType.String ) );
            }

            //pifu_email
            foreach (string name in Enum.GetNames(typeof(pifu_emailType)))
            {
                attributes.Add(SchemaAttribute.CreateSingleValuedAttribute(name, AttributeType.String));
            }

            return attributes;
        }

        #endregion implemented_base_class_methods

        #region private_methods

        // Objective : recursivly find the organisation for this group
        // Input : the parents of the cur group, the entire set of groups
        // Output : The MemberOf attributes are initalized

        private void FindOrg(HashSet<object> gParents,  ref Dictionary<string, Dictionary<string,PIFUGroup>> OUPGs)
        {
            foreach (string pID in gParents)
            {
                foreach (string key in OUPGs.Keys)
                {
                    if (OUPGs[key].ContainsKey(pID))
                    {
                        int pOrgLevel = OUPGs[key][pID].groupType;
                        bool pIsOrg = OUPGs[key][pID].isOrg;
                        bool pIsGrp = OUPGs[key][pID].isGrp;
                        //if (pIsGrp || pOrgLevel > 0)
                        //    FindOrg(OUPGs[key][pID].ParentGroups, ref OUPGs);

                        if (isGrp && pOrgLevel == 1)
                            FindOrganizionNumber(OUPGs[key][pID]);

                        if (pIsOrg && (isGrp || (groupType > pOrgLevel)|| groupType == -1))
                        {
                            if (!base.MemberOf.ContainsKey(pOrgLevel))
                                base.MemberOf.Add(pOrgLevel, new HashSet<object>());

                            base.MemberOf[pOrgLevel].Add(pID);
                        }
                        break;
                    }

                }
                
            }
        }

        private void FindOrganizionNumber (PIFUGroup PG)
        {
        if (PG.xmlGroup != null && PG.xmlGroup.extension != null && PG.xmlGroup.extension.pifu_id != null)
        {
            foreach (pifu_id pid in PG.xmlGroup.extension.pifu_id)
            {
                if (pid.typeSpecified && pid.type == pifu_idType.organizationNumber)
                    organizationNumber = "NO" + pid.pifu_value;
            }
        }
        }


        // Objective : add all the parents for this group
        // Input : ! = null
        // Output : ParentGroups is initalized
        private void HandleRealtionships( relationship[] rels )
        {
            foreach( relationship r in rels )
            {
                relationshipRelation rel = ( r.relationSpecified ) ? r.relation : relationshipRelation.Item1;
                //if( r.sourcedid.source.Value != source )
                //   Logger.Log.Info( "Warning: Adding relationship " + groupID + " from other source : " + r.sourcedid.source.Value + " for group " + ID );

                string groupID = Converter.createID( r.sourcedid );
                switch( rel )
                {
                    case relationshipRelation.Item1: //Parent
                        if( r.sourcedid == null )
                            throw new Exception( "Error : we have a relations without ID, bailing out on group " + base.ID );
                        if( !r.sourcedid.sourcedidtypeSpecified || ( r.sourcedid.sourcedidtype == sourcedidSourcedidtype.New ) ) //only look at active id
                            base.ParentGroups.Add( groupID );
                        break;

                    case relationshipRelation.Item3: //Known as
                        Debug.Assert( false, "Warning : we do not handle alias or \"known as\" realtionships, in production this will be silently ignored" );
                        break;
                }
            }
        }

        #endregion private_methods
    }
}