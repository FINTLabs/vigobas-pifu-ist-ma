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
    using Vigo.Bas.Utils;
    using System.Linq;

    //using System.Net;

    // Name : PIFUPerson
    // Description: Contains the complete XML <person> + additional/methods used during conversion
    public class PIFUPerson : PIFUObject
    {
        public const string typeName = "person";

        private const string GREP_PREFIX = "urn:mace:feide.no:go:grep:";

        private const string GREP_PREFIX_UUID = "urn:mace:feide.no:go:grep:uuid:";

        private const string PREFIX_FEIDE_GROUP = "urn:mace:feide.no:go:group:";

        private const string PREFIX_FEIDE_GROUPID = "urn:mace:feide.no:go:groupid:";

        private HashSet<object> EduPersonEntitlement = new HashSet<object>();
        private HashSet<object> EnhetUserType = new HashSet<object>();
        private HashSet<object> roles = new HashSet<object>();
        private string primaryRole;
        private string primaryOrgUnit;
        private string primaryClassGroup;
        private bool isContactPerson;
        private string contactTeacher;

        // Objective : Default Constructor Input : the type (defaults to group Description :
        // Used as a default constructor, mainly for later calling getSchema that gets the PIFU
        // Schema from the xsd
        public PIFUPerson()
            : base(typeName, new person())
        {
        }

        // Objective   : Constructor
        // Input       : unique ID, possible other IDs or null,
        //             : the corresponding PIFU xmlObject, the schema (retrived from FIM),
        //             : the schema retrievd from FIM
        // Output      : Initalized = true;
        // Description : Initialized the object for later retriving the values for import into CS
        public PIFUPerson(string id, HashSet<object> otherids, person person, Schema schema, List<string> csAttributes, bool isContact)
            : base(id, otherids, typeName, person, schema, csAttributes, ((person.recstatusSpecified) ? Converter.getCSModificationType(person.recstatus) : Converter.getCSModificationType(recstatus.Item1)))
        {
            isContactPerson = isContact;
        }

        // Objective   : Find all groups this person belongs and update organisational structure, which roles the person have and on which unit it have these
        // Output      : The person has a populated, enhetusertype, memberof, parents and roles
        public void HandleMembership(string orgUnit, bool usePersonCode, Dictionary<string, string> SSNToPersonCode, ref Dictionary<string, Dictionary<string, PIFUGroup>> OUPGs)
        {
            foreach (var pifuGroup in OUPGs[orgUnit].Values)
            {
                foreach (var pm in pifuGroup.MemberGroups.Values)
                {
                    if (pm.Members.Contains(ID))
                    {
                        base.ParentGroups.Add(pifuGroup);

                        if (pifuGroup.groupType == 3)
                        {
                            string pifuGroupId = pifuGroup.ID;
                            contactTeacher = Converter.getContactTeacherIdAsSourcedid(pifuGroupId, usePersonCode, SSNToPersonCode);
                        }

                        roleRoletype rt = pm.RoleType;

                        string[] employeeRoles = { "faculty", "staff" };

                        string feideRole = Converter.ConvertFromPifuRolesToFeideRoles(Converter.ConvertFromValueToString(rt));

                        if (pifuGroup.isOrg)
                        {
                            //string enhetUsertypeEmployee = "employee" + Converter.delimiterAttributes + pifuGroup.ID;

                            if (!MemberOf.ContainsKey(pifuGroup.groupType))
                            {
                                MemberOf.Add(pifuGroup.groupType, new HashSet<object> { pifuGroup.ID });
                            }
                            else if (!MemberOf[pifuGroup.groupType].ToString().Equals(pifuGroup.ID))
                            {
                                MemberOf[pifuGroup.groupType].Add(pifuGroup.ID);
                            }

                            if (pifuGroup.MemberOf.ContainsKey(0) && !MemberOf.ContainsKey(0))
                            {
                                var moValue = pifuGroup.MemberOf[0];
                                MemberOf.Add(0, moValue);
                            }
                            string enhetusertype = feideRole + Converter.delimiterAttributes + pifuGroup.ID;
                            EnhetUserType.Add(enhetusertype);

                            //if (employeeRoles.Contains(feideRole) && !EnhetUserType.Contains(enhetUsertypeEmployee))
                            //{
                            //    EnhetUserType.Add(enhetUsertypeEmployee);
                            //}

                        }

                        // Necessary in case a person has more than one usertype assosiated with the same school
                        if (pifuGroup.isGrp && pifuGroup.MemberOf.ContainsKey(1))
                        {
                            foreach (string unitID in pifuGroup.MemberOf[1])
                            {
                                string enhetusertype = feideRole + Converter.delimiterAttributes + unitID;
                                string enhetUsertypeEmployee = "employee" + Converter.delimiterAttributes + unitID;

                                if (!EnhetUserType.Contains(enhetusertype))
                                {
                                    EnhetUserType.Add(enhetusertype);
                                }

                                //if (employeeRoles.Contains(feideRole) && !EnhetUserType.Contains(enhetUsertypeEmployee))
                                //{
                                //    EnhetUserType.Add(enhetUsertypeEmployee);
                                //}

                            }

                        }
                        roles.Add(feideRole);
                        //Everyone has the member role, maybe this will change with nextofkin persons
                        roles.Add("member");

                        if (employeeRoles.Contains(feideRole) && !roles.Contains("employee"))
                        {
                            roles.Add("employee");
                        }

                        // find primary role
                        if (roles.Contains("faculty"))
                        {
                            primaryRole = "faculty";
                        }
                        else if (roles.Contains("student"))
                        {
                            primaryRole = "student";
                        }
                        else if (roles.Contains("staff"))
                        {
                            primaryRole = "staff";
                        }

                        //Add primary orgunit based on primary role
                        foreach (var enhetUserType in EnhetUserType)
                        {
                            var enhetUserTypeRole = enhetUserType.ToString().Split(';')[0];
                            if (enhetUserTypeRole.Equals(primaryRole))
                            {
                                primaryOrgUnit = enhetUserType.ToString().Split(';')[1];
                                // add class group to students
                                if (enhetUserTypeRole == "student" && pifuGroup.isGrp && pifuGroup.groupType == 1)
                                {
                                    primaryClassGroup = pifuGroup.groupFriendlyName;
                                }
                            }
                        }

                        if (pifuGroup.isGrp)
                        {
                            switch (pifuGroup.groupType)
                            {
                                case 1:
                                case 2:
                                    string grouptype = ((pifuGroup.groupType == 1) ? "b" : "u");
                                    string urlencodedfullname = (pifuGroup.groupFriendlyName != null) ? Uri.EscapeDataString(pifuGroup.groupFriendlyName) : "";
                                    string orgnumber = pifuGroup.organizationNumber;
                                    string urlencodedgroupid = (pifuGroup.localGroupID != null) ? Uri.EscapeDataString(pifuGroup.localGroupID) : "";
                                    string urlencodedgroupidlowercase = (pifuGroup.localGroupID != null) ? Uri.EscapeDataString(pifuGroup.localGroupID.ToLower()) : "";
                                    string timespan = pifuGroup.startDateTime + Converter.delimiterIDValues + pifuGroup.endDateTime;

                                    string groupstring = PREFIX_FEIDE_GROUP + grouptype + Converter.delimiterIDValues;
                                    groupstring += ((pifuGroup.groupType == 1) ? "" : pifuGroup.grepCodeShortForm) + Converter.delimiterIDValues;
                                    groupstring += orgnumber + Converter.delimiterIDValues + urlencodedgroupid + Converter.delimiterIDValues;
                                    groupstring += timespan + Converter.delimiterIDValues;
                                    groupstring += ((Converter.ConvertFromValueToString(rt).ToLower().Equals("learner")) ? "student" : "faculty") + Converter.delimiterIDValues + urlencodedfullname;
                                    EduPersonEntitlement.Add(groupstring);

                                    string groupidstring = PREFIX_FEIDE_GROUPID + grouptype + Converter.delimiterIDValues;
                                    groupidstring += orgnumber + Converter.delimiterIDValues + urlencodedgroupidlowercase + Converter.delimiterIDValues + timespan;
                                    EduPersonEntitlement.Add(groupidstring);

                                    // add grepcodes for 'fag' for teachers 
                                    foreach (var enhetUserType in EnhetUserType)
                                    {
                                        var enhetUserTypeRole = enhetUserType.ToString().Split(';')[0];
                                        if (enhetUserTypeRole == "faculty")
                                        {
                                            foreach (string code in pifuGroup.grepCode)
                                            {
                                                string grepcode = ((code.StartsWith("http")) ? GREP_PREFIX : GREP_PREFIX_UUID) + code;
                                                EduPersonEntitlement.Add(grepcode);
                                            }
                                        }
                                    }
                                    break;
                                case 4:
                                case 5:
                                case 6:
                                case 7:
                                    foreach (string code in pifuGroup.grepCode)
                                    {
                                        string grepcode = ((code.StartsWith("http")) ? GREP_PREFIX : GREP_PREFIX_UUID) + code;
                                        EduPersonEntitlement.Add(grepcode);
                                    }
                                    break;
                            }
                        }
                    }

                }
            }
        }

        public void HandleContactPersonRole()
        {
            if (isContactPerson)
            {
                primaryRole = "contactperson";
                roles.Add("contactperson");
            }
        }
        public string GetPrimaryRole()
        {
            return primaryRole;
        }


        #region implemented_base_class_methods

        protected override Dictionary<string, object> GetAttributeValues()
        {
            Dictionary<string, object> retur = new Dictionary<string, object>();
            retur.Add( "EduPersonEntitlement", EduPersonEntitlement );  
            retur.Add( "EnhetUserType", EnhetUserType );
            retur.Add( "Roles", roles);
            retur.Add("primaryRole", primaryRole);
            retur.Add("primaryClassGroup", primaryClassGroup);
            retur.Add("primaryOrgUnit", primaryOrgUnit);
            retur.Add("contactTeacher", contactTeacher);


            //populate shortcut attributes
            person xmlPerson = ( person )base.xmlSource;
            if( xmlPerson.extension != null )
            {
                if( xmlPerson.extension.pifu_tel != null )
                    foreach( var a in base.HandlePifu_tel( xmlPerson.extension.pifu_tel ) )
                        retur.Add( a.Key, a.Value );

                if( xmlPerson.extension.pifu_adr != null )
                    foreach( var a in base.HandlePifu_adr( xmlPerson.extension.pifu_adr ) )
                        retur.Add( a.Key, a.Value );

                //if (xmlPerson.extension.pifu_email != null)
                //    foreach (var a in base.handlePifu_email(xmlPerson.extension.pifu_email))
                //        retur.Add(a.Key, a.Value);
            }
            //handle different userids
            if( xmlPerson.userid != null )
            {
                foreach( var id in xmlPerson.userid )
                {
                    switch (id.useridtype)
                    {
                        case useridtype.personNIN:
                            {
                                var ssnValue = id.Value;
                                retur.Add("ssn", ssnValue);
                                var ssnValidator = new Vigo.Bas.Utils.SocialSecurityNumber.SocialSecurityNumberValidator();
                                var ssnValidatorResult = ssnValidator.ValidateOne(ssnValue);
                                var ssnValidatorResultMessage = ssnValidatorResult.Message;
                                retur.Add("ssnValidation", ssnValidatorResultMessage);
                                break;
                            }
                        case useridtype.workforceID:
                            {
                                var workforceIDValue = id.Value;
                                retur.Add("workforceID", workforceIDValue);
                                break;
                            }
                        case useridtype.studentID:
                            {
                                var studentIDValue = id.Value;
                                retur.Add("studentID", studentIDValue);
                                break;
                            }
                        case useridtype.sisID:
                            {
                                var sisIDValue = id.Value;
                                retur.Add("sisID", sisIDValue);
                                break;
                            }
                        case useridtype.username:
                            {
                                var usernameValue = id.Value;
                                retur.Add("username", usernameValue);
                                break;
                            }
                        case useridtype.personFIN:
                            {
                                var personFINValue = id.Value;
                                retur.Add("personFIN", personFINValue);
                                break;
                            }
                        case useridtype.personLIN:
                            {
                                var personLINValue = id.Value;
                                retur.Add("personLIN", personLINValue);
                                break;
                            }
                    }
                }
            }

            return retur;
        }

        protected override HashSet<SchemaAttribute> GetSchemaAttributes()
        {
            HashSet<SchemaAttribute> attributes = new HashSet<SchemaAttribute>();

            attributes.Add( SchemaAttribute.CreateMultiValuedAttribute( "EduPersonEntitlement", AttributeType.String ) );
            attributes.Add( SchemaAttribute.CreateMultiValuedAttribute( "EnhetUserType", AttributeType.String ) );
            attributes.Add(SchemaAttribute.CreateMultiValuedAttribute("Roles", AttributeType.String));
            attributes.Add( SchemaAttribute.CreateSingleValuedAttribute( "primaryRole", AttributeType.String ) );
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("primaryClassGroup", AttributeType.String));
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("primaryOrgUnit", AttributeType.Reference));
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("ssn", AttributeType.String));
            attributes.Add( SchemaAttribute.CreateSingleValuedAttribute("ssnValidation", AttributeType.String));
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("workforceID", AttributeType.String));
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("studentID", AttributeType.String));
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("sisID", AttributeType.String));
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("username", AttributeType.String));
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("personFIN", AttributeType.String));
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("personLIN", AttributeType.String));
            attributes.Add(SchemaAttribute.CreateSingleValuedAttribute("contactTeacher", AttributeType.Reference));

            //pifu_adr
            foreach ( string name in Enum.GetNames( typeof( pifu_adrType ) ) )
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
            //foreach (string name in Enum.GetNames(typeof(pifu_emailType)))
            //{
            //    attributes.Add(SchemaAttribute.CreateSingleValuedAttribute(name, AttributeType.String));
            //}

            return attributes;
        }

        #endregion implemented_base_class_methods
    }
}