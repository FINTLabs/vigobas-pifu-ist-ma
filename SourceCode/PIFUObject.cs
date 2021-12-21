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
    using System.Collections;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using Vigo.Bas.ManagementAgent.Log;

    // Name : PIFUObject
    // Description: Is the abstract parent to PIFUGroup and PIFUPerson and representes common methods and attributes
    public abstract class PIFUObject
    {
        private SchemaType schemaType;  //The SchemaType for this objectType (from FIM Schemas)

        protected static readonly string orgLevelPrefix = "orgLevel" + Converter.delimiterNumbers; //this is also in effect he "typeName" of the PIFUOrrgGroup for a certain level

        public static readonly string MemberOfPrefix = "MemberOf" + Converter.delimiterBelongs + orgLevelPrefix;

        public bool Initalized { get; private set; } //Can this object be converted to a CS object

        public string objectType { get; private set; } //CS objectType (person/group/membership/orgLevel#X)

        public object xmlSource { get; private set; }  //exported to FIM-CS via Reflection

        public List<string> attributesToCS; //List of which attributes to be exported to CS 

        public ObjectModificationType action { get; private set; }  //add, update, delete the objectType in CS

        //These are exported to FIM-CS (via CSAttributes)
        public string ID { get; private set; } //Ancor and DN

        public HashSet<object> OtherSystemsID { get; private set; } //id:Value; source:Value

        public Dictionary<int, HashSet<object>> MemberOf { get; protected set; } //<groupType, List of IDs>  will create Attribute "MemberOfPrefix + orgLevel of X : X.ID"

        public HashSet<object> ParentGroups { get; protected set; }

        // Objective : Default Constructor Input : Output : Initalized = false; Description :
        // Initalize the variables
        public PIFUObject()
        {
            objectType = string.Empty;
            ID = string.Empty;
            OtherSystemsID = new HashSet<object>();
            MemberOf = new Dictionary<int, HashSet<object>>();
            ParentGroups = new HashSet<object>();
            Initalized = false;
        }

        // Objective : Constructor Input : The CS object Name, the corresponding PIFU xmlObject
        // Output : Initalized = false; Description : Initalize the object for retriving the xmlSchema
        public PIFUObject( string t, object xmlObject )
            : this()
        {
            objectType = t;
            xmlSource = xmlObject;
            Initalized = false;
        }

        // Objective   : Constructor
        // Input       : unique ID,
        //             : possible other IDs or null,
        //             : the CS object name,
        //             : the corresponding PIFU xmlObject,
        //             : the schema (retrived from FIM),
        //             : which attributes to import to CS
        //             : what to do with the object in terms of CSModificationType (Add/delete/update), default is "add"
        // Output      : Initalized = true;
        // Description : Initialized the object for later retriving the values for import into CS
        public PIFUObject( string id, HashSet<object> otherId, string type, object xmlObject, Schema s, List<string> attributesToCSList, ObjectModificationType recstatus = ObjectModificationType.Add )
            : this( type, xmlObject )
        {
            if( id == null )
                throw new Exception( "Error : ID cannot be null, bailing out" );
            ID = id;
            OtherSystemsID = ( otherId != null ) ? otherId : new HashSet<object>();
            objectType = type;
            xmlSource = xmlObject;
            attributesToCS = attributesToCSList;
            action = recstatus;
            schemaType = s.Types[objectType];
            Initalized = true;
        }

        // Objective : Retrieve a FIM-CS "Entry" for import into FIM-CS Require : Initalized == true 
        // Input : 
        // Output : the values of the common attributes
        public CSEntryChange GetEntry()
        {
            if( !Initalized )
                throw new Exception( "Error : The object is not initalized properly" );

            //This variable is used as a intermediat step before creating CSAttributes, the schema can further limit what will be exported to CS
            Dictionary<string, object> CSAttributes = GetAttributeValues(); //call the method of the children to populate CSAttributes and to do final changes before exporting to CS

            Reflector R = new Reflector( xmlSource ); //Get the values by reflection

            Dictionary<string, SchemaAttribute> attr = new Dictionary<string, SchemaAttribute>(); //Holds all attributes we are interested in
            foreach( var attrib in schemaType.Attributes )
                attr.Add( attrib.Name, attrib );

            Dictionary<string, object> retur = CSAttributes; //hack to be able to pass a dynamic property as a ref
            R.ResolveNameAndValue( ref retur, attr );

            //Set the attributes that are common for all PIFUObjects
            CSAttributes.Add( "OtherSystemsID", OtherSystemsID );
            CSAttributes.Add( "ParentGroups", ParentGroups );
            foreach( var p in MemberOf )
                CSAttributes.Add( MemberOfPrefix + p.Key.ToString(), p.Value );

            //Write some Debug Information, it is a lot of DATA!!!
            Logger.Log.Info( "Creating CS entry for : <" + objectType + "> DN=Anchor : " + ID + " with action : " + action.ToString() );
            if( !Globals.nrObjects.ContainsKey( objectType ) )
                Globals.nrObjects.Add( objectType, 0 );
            Globals.nrObjects[objectType]++;
            Globals.nrAttributes += CSAttributes.Count;

            Logger.Log.Debug( "ID[Anchor and DN] : " + ID );
            foreach( var e in CSAttributes )
            {
                if( schemaType.Attributes.Contains( e.Key ) && e.Value != null )
                {
                    if( schemaType.Attributes[e.Key].IsMultiValued )
                    {
                        foreach( var v in ( ICollection<object> )e.Value )
                            Logger.Log.Debug( e.Key + "[*] : " + v.ToString() );
                    }
                    else
                        Logger.Log.Debug( e.Key + " : " + e.Value );
                }
            }

            if( !Globals.TestMode ) //Create the CS object if we are in a FIM context
            {
                CSEntryChange CS = CSEntryChange.Create();
                CS.ObjectType = objectType;
                CS.ObjectModificationType = action;
                CS.AnchorAttributes.Add( AnchorAttribute.Create( "ID", ID ) );
                CS.DN = ID;

                foreach( var e in CSAttributes )
                {
                    if( schemaType.Attributes.Contains( e.Key ) && e.Value != null && (attributesToCS.Count == 0 || attributesToCS.Contains(e.Key)))
                    {
                        if( schemaType.Attributes[e.Key].IsMultiValued )
                        {
                            if( ( ( HashSet<object> )e.Value ).Count > 0 )  //skip null sets
                            {
                                IList<object> value = ( ( HashSet<object> )e.Value ).ToList();
                                CS.AttributeChanges.Add( AttributeChange.CreateAttributeAdd( e.Key, value ) );
                            }
                        }
                        else
                            CS.AttributeChanges.Add( AttributeChange.CreateAttributeAdd( e.Key, e.Value ) );
                    }
                }
                return CS;
            }
            else //if not in FIM context
                return null;
        }

        // Objective : Retrieve FIM Schema from XSD 
        // Require : PIFUObject( string t, object xmlObject ) 
        // Output : a schemaType that list all the possible attribute names and types for this objectType
        // 
        public SchemaType GetSchema()
        {
            SchemaType retur = SchemaType.Create( objectType, true ); //lock so we can't choose another anchor
            HashSet<SchemaAttribute> attributes = GetSchemaAttributes(); //get the attributes from the children

            attributes.Add( SchemaAttribute.CreateAnchorAttribute( "ID", AttributeType.String ) );
            attributes.Add( SchemaAttribute.CreateMultiValuedAttribute( "OtherSystemsID", AttributeType.String ) );
            attributes.Add( SchemaAttribute.CreateMultiValuedAttribute( "ParentGroups", AttributeType.Reference ) );
            for( int i = 0; i < Converter.orgLevelMax; i++ )
                attributes.Add( SchemaAttribute.CreateMultiValuedAttribute( MemberOfPrefix + i, AttributeType.Reference ) );

            Reflector R = new Reflector( xmlSource );
            R.ResolveNameAndType( ref attributes );

            foreach( SchemaAttribute attrib in attributes )
                retur.Attributes.Add( attrib );

            //Write som debug info (the schema)
            Logger.Log.Debug( "Creating schema <" + retur.Name + ">" );
            Logger.Log.Debug( retur.AnchorAttributes[0].Name + "[Anchor and DN]  : " + retur.AnchorAttributes[0].DataType.ToString() );
            foreach( SchemaAttribute attrib in retur.Attributes )
                Logger.Log.Debug( attrib.Name + ( ( attrib.IsMultiValued ) ? "[*] : " : " : " ) + attrib.DataType.ToString() );

            return retur;
        }

        #region abstract

        protected abstract HashSet<SchemaAttribute> GetSchemaAttributes(); // This should be implemented in the sub classes to create the schemaAttributes for that child

        protected abstract Dictionary<string, object> GetAttributeValues(); // This should be implemented in the sub classes to retrive any special members

        #endregion abstract

        #region protected helper_methods

        // Objective : Helper Method to return all pifu extensions pifu_ID (ala ORG Number and
        // vigoNumber och grepcoder) Input : != null Output : a Dictionary with Key =
        // attributname , Value = value of the attribute and possiblöe sub attributes delimited
        // by Converter.delimiterValues (,) Description : will fail if pid.pifu_uniq is not
        // respect in source
        protected Dictionary<string, object> HandlePifu_id( pifu_id[] pid )
        {
            HashSet<string> uniq = new HashSet<string>();
            Dictionary<string, object> retur = new Dictionary<string, object>();
            foreach( pifu_id id in pid )
            {
                if( id.typeSpecified )
                {
                    string key = Converter.ConvertFromValueToString( id.type );
                    if( !uniq.Add( key ) && Converter.ConvertFromValue( id.pifu_unique ) )
                        throw new Exception( "Error: " + key + " is supposed to be unique, bailing out on Groupd ID" + ID );

                    if( retur.ContainsKey( key ) )
                        retur[key] += Converter.delimiterValues + id.pifu_value;
                    else
                        retur.Add( key, id.pifu_value );
                }
            }
            return retur;
        }

        // Objective : Helper Method to return pifu extensions telephone numbers Input : != null
        // Output : a Dictionary with Key = attributname , Value = value of the attribute and
        // possible sub attributes delimited by Converter.delimiterValues (,)
        protected Dictionary<string, object> HandlePifu_tel( pifu_tel[] ptel )
        {
            Dictionary<string, object> retur = new Dictionary<string, object>();
            foreach( pifu_tel tel in ptel )
            {
                if( tel.typeSpecified )
                {
                    string key = Converter.ConvertFromValueToString( tel.type );
                    if( retur.ContainsKey( key ) )
                        retur[key] += Converter.delimiterValues + tel.Value;
                    else
                        retur.Add( key, tel.Value );
                }
            }
            return retur;
        }

        // Objective : Helper Method to return pifu extensions addresses Input : != null Output
        // : a Dictionary with Key = attributname , Value = value of the attribute and possible
        // sub attributes delimited by Converter.delimiterValues (,)
        protected Dictionary<string, object> HandlePifu_adr( pifu_adr[] padr )
        {
            Dictionary<string, object> retur = new Dictionary<string, object>();
            foreach( pifu_adr id in padr )
            {
                if( id.typeSpecified )
                {
                    string key;
                    string value;

                    if( id.Text != null )
                    {
                        key = Converter.ConvertFromValueToString( id.type ) + Converter.delimiterElements + "Text";
                        value = id.Text.ToString();

                        if( retur.ContainsKey( key ) )
                            retur[key] += Converter.delimiterValues + value;
                        else
                            retur.Add( key, value );
                    }

                    if( id.adr != null )
                    {
                        foreach( PropertyDescriptor prop in TypeDescriptor.GetProperties( id.adr ) )
                        {
                            if( !Converter.isSpecifier( prop ) )
                            {
                                string curName = prop.DisplayName;
                                PropertyInfo pi = prop.ComponentType.GetProperty( curName );
                                object curElementValue = ( pi != null ) ? pi.GetValue( id.adr, null ) : null;
                                if( curElementValue != null )
                                {
                                    key = Converter.ConvertFromValueToString( id.type ) + Converter.delimiterElements + curName;
                                    value = string.Empty;

                                    if( pi.PropertyType.IsArray )
                                        value += string.Join( Converter.delimiterValues.ToString(), ( string[] )curElementValue );
                                    else if( !String.IsNullOrWhiteSpace( curElementValue.ToString() ) )
                                        value = curElementValue.ToString();

                                    if( retur.ContainsKey( key ) )
                                        retur[key] += Converter.delimiterValues + value;
                                    else
                                        retur.Add( key, value );
                                }
                            }
                        }
                    }
                }
            }
            return retur;
        }

        protected Dictionary<string, object> handlePifu_email(pifu_email[] padr)
        {
            // Objective : Helper Method to return pifu extensions email
            //Input : != null
            //Output: a Dictionary with Key = attributname , Value = value of the attribute and possible sub attributes delimited by Converter.delimiterValues (,)
            Dictionary<string, object> retur = new Dictionary<string, object>();
            foreach (pifu_email id in padr)
            {
                if (id.typeSpecified)
                {
                    string key = Converter.ConvertFromValueToString(id.type);
                    // string value = "priority" + Converter.delimiterIDValues + id.priority + Converter.delimiterAttributes + id.Value;
                    string value = id.Value;
                    if (retur.ContainsKey(key))
                        retur[key] += Converter.delimiterValues + value;
                    else
                        retur.Add(key, value);
                }
            }

            return retur;
        }

        #endregion protected helper_methods
    }
}