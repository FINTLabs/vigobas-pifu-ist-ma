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
    using System.Reflection;

    // Name       : Reflector
    // Description: "Converts" a object into a "format" suitable for storing in FIM-CS
    // Limitation : Due to dependency on Converter and outputformat of xsd.exe the class is not a general objectreader
    // It should be possible to make it general with some supporting structure (a converter interface) and some minor tweaks
    //
    // This is black magic (ie bad code) and is not for the faint of heart :-)

    public class Reflector
    {
        private object obj;
        private Dictionary<string, SchemaAttribute> schemaAttributes;
        public static readonly string attributPrefix = "pifuXml" + Converter.delimiterNumbers;

        // Objective   : Constructor, create a reflkector for this object
        // Input       : object, object != object[]
        // Output      : a reflector for input object
        // Description : Eternal pain and misery will come if the object is an array (it can can however be a class etc that consists of arrays)
        public Reflector( object inobj )
        {
            obj = inobj;
        }

        // Objective   : resolve the name and types for all attributes of "xmlSource"
        // Input       : returning object , typeof(xmlSource)
        // Output      : a SchemaAttributes with names and types representing xmlSource
        public void ResolveNameAndType( ref HashSet<SchemaAttribute> entry )
        {
            object retur = entry;
            string line = string.Empty;
            ResolveNameAndObject( ref retur, obj, false, ref line );
        }

        // Objective   : resolve the name and values for all attributes of "xmlSource"
        // Input       : returning object , xmlSource
        // Output      : a object with names and values representing xmlSource
        public void ResolveNameAndValue( ref Dictionary<string, object> csentry, Dictionary<string, SchemaAttribute> s )
        {
            schemaAttributes = s;
            HashSet<object> inclusions = null;

            //Optimizations do not parse the objects that is not in the schema.
            inclusions = new HashSet<object>();
            foreach( var key in schemaAttributes.Values )
            {
                if( key.Name.StartsWith( attributPrefix ) )
                {
                    string name = key.Name.Substring( attributPrefix.Length );

                    string wholename = string.Empty;
                    foreach( string n in name.Split( Converter.delimiterElements ) )
                    {
                        if( wholename != string.Empty )
                            wholename += Converter.delimiterElements;

                        wholename += n;
                        inclusions.Add( n );
                        inclusions.Add( wholename );
                    }
                }
            }

            if( inclusions.Count == 0 ) //The scheama does not contaion any element
                return;

            object retur = csentry;
            string line = string.Empty;
            ResolveNameAndObject( ref retur, obj, true, ref line, inclusions );
        }

        #region private_methods

        // Objective   : resolve ALL Names and ObjectValues or Types for current object
        // Input       : entry , returning object
        // obj, current object
        // entryIsCSEntryChange, should resolve value or type?
        // MultiValue, a string representation of the current values in a array object
        // MultiValuePath, the Name of the array object
        // path, current path (depth of tree)
        // isMultiValue, is the current obj an array or any objects below in the tree of type array?
        //
        // Output      : entry filled with names and values or names and types
        // Description : Recursivly goes trough the object tree and retrives the names and values or types, used on xmlSource to get the values and types from the xml
        private void ResolveNameAndObject( ref object entry, object obj, bool entryIsCSEntryChange, ref string MultiValue, HashSet<object> inclusions = null, string MultiValuePath = null, string path = null, bool isMultiValue = false )
        {
            Type objType = obj.GetType();
            PropertyDescriptorCollection list = TypeDescriptor.GetProperties( obj ); //list all properties (methods of a object)
            if( list.Count <= 0 || objType.Namespace == "System" )  // We reached the leaf of the tree
            {
                if( entryIsCSEntryChange )  //Save the value of the object
                {
                    Dictionary<string, object> CSEntry = ( Dictionary<string, object> )entry;
                    if( isMultiValue )    //add the name: value to the current "mulitLine"
                        saveAttributeMultiValue( obj, path, MultiValuePath, ref MultiValue );
                    else //"normal" object
                        saveAttributeValue( objType, obj, path, ref CSEntry );
                }
                else    //Save the type of the object, here we don't care about multiLine since we handle this inside the saveAttributeSchemaType
                {
                    HashSet<SchemaAttribute> SEntry = ( HashSet<SchemaAttribute> )entry;
                    saveAttributeSchemaType( objType, path, isMultiValue, MultiValuePath, ref SEntry );
                }
            }
            else // The object contians more objects
            {
                foreach( PropertyDescriptor prop in list )  // foreach property of the object recurse
                {
                    string curName;
                    string curPath;
                    bool currentIsMultiValue;
                    object curElementValue;

                    ParseProperty( prop, path, isMultiValue, out curPath, out currentIsMultiValue, out curName );
                    ParseObject( prop, obj, out curElementValue, !entryIsCSEntryChange );
                    string curMultiValuePath = ( isMultiValue ) ? MultiValuePath : curPath;

                    if( inclusions != null && !inclusions.Contains( curMultiValuePath ) )  //if we have a inclusionslits and we are not part of it
                        continue;

                    if( curElementValue != null && !Converter.isSpecifier( prop ) && ( Converter.isSpecified( prop, obj ) || !entryIsCSEntryChange ) )
                    {
                        if( prop.PropertyType.IsArray ) // we have an array of the same object types
                        {
                            foreach( object val in ( object[] )curElementValue ) // we need to go through all elements of the array and recurse further, these elements are by definitian MultiValue
                            {
                                string multiLine = string.Empty;
                                ResolveNameAndObject( ref entry, val, entryIsCSEntryChange, ref multiLine, inclusions, curMultiValuePath, curPath, currentIsMultiValue ); //recurse down until we reach an actual value
                                if( entryIsCSEntryChange )  //if we should save the values these are now stored in a multiLine
                                {
                                    //Here we have two options :
                                    // 1. current obj is an array inside an array, that is we should not save the values as an new element on curMultiValuePath but instead
                                    // add the current multiLine to MulitValue
                                    // 2. We are "back" where we started and have a completed multiLine
                                    if( curMultiValuePath != curPath )   //1
                                    {
                                        //saveAttributeMultiValue( val, curPath, curMultiValuePath, ref MultiValue );
                                        if( MultiValue != string.Empty )
                                            MultiValue += Converter.delimiterAttributes;

                                        MultiValue += multiLine;
                                    }
                                    else    //2
                                    {
                                        //CSEntryChange CSEntry = ( CSEntryChange )entry;
                                        Dictionary<string, object> CSEntry = ( Dictionary<string, object> )entry;
                                        saveAttributeValue( multiLine.GetType(), multiLine, curMultiValuePath, ref CSEntry );
                                    }
                                }
                            }
                        }
                        else  //no array so use the current object and recurse further
                            ResolveNameAndObject( ref entry, curElementValue, entryIsCSEntryChange, ref MultiValue, inclusions, curMultiValuePath, curPath, currentIsMultiValue );
                    }
                }
            }
        }

        // Objective   : parse the properties for the current object __ helper to ResolveNAmeandObject
        // Input       : PropertyDescriptor prop of the current object, current path, current MultiVale
        // Output      : path (to the object), is the current object a mulitvalue and the name of the current object
        // Description : Effectivly determines what the names should be for the object (prop.DisplayName)
        private void ParseProperty( PropertyDescriptor prop, string path, bool MultiValue, out string curPath, out bool curMultiValue, out string curName )
        {
            curName = prop.DisplayName;
            curPath = ( path == null ) ? curName : path + Converter.delimiterElements + curName;

            curMultiValue = ( MultiValue || prop.PropertyType.IsArray );

            /* to beautyfi schemas but risk creating bugs enable this
            if (curName != "Value" && curName != "Text" && curName != "source")
                curPath = (path == null) ? curName : path + "." + curName;
            else
              curPath = path;
            */
        }

        // Objective   : parse the values for the current object __ helper to ResolveNAmeandObject
        // Input       : PropertyDescriptor prop of the current object, current object, createObjectValue if not exists
        // Output      : next object in the path
        // Description : if a object exist it gets its value, if it does not exist it creates it and initalize the obkect (this is only used to get the types) for the getchema
        private void ParseObject( PropertyDescriptor prop, object obj, out object curElementValue, bool createObjectIfNull = false )
        {
            string curName = prop.DisplayName;

            PropertyInfo pi = prop.ComponentType.GetProperty( curName );
            curElementValue = ( pi != null ) ? pi.GetValue( obj, null ) : null;

            if( createObjectIfNull && curElementValue == null )
            {
                if( pi.PropertyType.IsArray )
                {
                    curElementValue = Activator.CreateInstance( pi.PropertyType, 1 ); //Crate the arry with one element

                    // Create the element
                    if( pi.PropertyType.GetElementType().Namespace != "System" )
                        ( ( object[] )curElementValue )[0] = Activator.CreateInstance( pi.PropertyType.GetElementType() );
                    else
                        ( ( object[] )curElementValue )[0] = createSystemValue( pi.PropertyType.GetElementType() );
                }
                else
                {
                    if( pi.PropertyType.Namespace != "System" )
                        curElementValue = Activator.CreateInstance( pi.PropertyType );
                    else
                        curElementValue = createSystemValue( pi.PropertyType );
                }
            }
        }

        // Objective   : Save the key(name) and the type for the object
        // Input       : PropertyDescriptor prop of the current object, current path, if it is multivalue, multi valuepath, returning object
        // Output      : updated returning SEntry
        // Description : saves key and type for singlevalue, for multivalue only the first occurance of the key is saved and the type is always string
        private void saveAttributeSchemaType( Type prop, string path, bool isMultiValue, string MultiValuePath, ref HashSet<SchemaAttribute> SEntry )
        {
            AttributeType attribType = GetCSAttribType( prop );
            attribType = ( isMultiValue ) ? AttributeType.String : attribType;

            string key, valueName;
            parseKeyAndValueName( out key, out valueName, isMultiValue, path, MultiValuePath );

            key = attributPrefix + key;  //add the prefix if any
            SchemaAttribute CSAttrType = ( isMultiValue ) ? SchemaAttribute.CreateMultiValuedAttribute( key, attribType ) : SchemaAttribute.CreateSingleValuedAttribute( key, attribType );
            SEntry.Add( CSAttrType );
        }

        // Objective   : parse path or multivaluepath into a name and and key
        // Input       : is multivalue , path, multivalue path
        // Output      : key to use to save the attribute in FIM-CS, the name of the element (if multiValue)
        private void parseKeyAndValueName( out string key, out string valueName, bool isMultiValue, string path, string MultiValuePath )
        {
            key = ( isMultiValue ) ? MultiValuePath : path;
            valueName = ( isMultiValue ) ? path.Replace( MultiValuePath, string.Empty ) : string.Empty;
            if( valueName != string.Empty )
                valueName = valueName.Substring( 1 );
        }

        // Objective   : Save the key(name) and the value for the object
        // Input       : PropertyDescriptor prop of the current object, current path, returning object
        // Output      : updated returning object
        // Description : saves key and values (multiValues are automatically handled)
        private void saveAttributeValue( Type prop, object obj, string key, ref Dictionary<string, object> attributes )
        {
            AttributeType attribType = GetCSAttribType( prop );
            object add = ( attribType == AttributeType.String ) ? Converter.ConvertFromValueToString( obj ) : obj;

            if( schemaAttributes == null )
                throw new Exception( "Error: Schema is null" );

            key = attributPrefix + key; //add the prefix if any
            if( schemaAttributes.ContainsKey( key ) )
            {
                if( schemaAttributes[key].IsMultiValued )
                {
                    if( !attributes.ContainsKey( key ) )
                        attributes.Add( key, new HashSet<object>() );

                    ( ( HashSet<object> )attributes[key] ).Add( add );
                }
                else
                {
                    if( attributes.ContainsKey( key ) )
                        throw new Exception( "Error: we are trying to add more than one value to a non multivalue" );
                    attributes.Add( key, add );
                }
            }
        }

        // Objective   : Intermediatly save the key(name) and the value for the object in a multivalue
        // Input       : current object, current path, MultiValuePath, returning object
        // Output      : updated "multiLine"
        // Description : this method defines how a multivalue (a attribute consisting of multiple values) stores names and values (name:value)
        private void saveAttributeMultiValue( object obj, string path, string MultiValuePath, ref string MultiValue )
        {
            string key, valueName;
            parseKeyAndValueName( out key, out valueName, true, path, MultiValuePath );

            if( MultiValue != string.Empty )
                MultiValue += Converter.delimiterAttributes;

            MultiValue += ( valueName != string.Empty ) ? valueName + Converter.delimiterIDValues + Converter.ConvertFromValueToString( obj ) : Converter.ConvertFromValueToString( obj );
        }

        // Objective   : get the corrosponding CSAttribType for a property___ helperfunction to saveAttributeSchemaType(), saveAttributeValue and createSystemValue
        // Input       : type
        // Output      : AttibutType
        // Description : Currently String us used for all that is not interger or boolean
        private AttributeType GetCSAttribType( Type prop )
        {
            if( prop.Equals( typeof( bool ) ) )
                return AttributeType.Boolean;
            if( prop.Equals( typeof( int ) ) )
                return AttributeType.Integer;

            return AttributeType.String;
        }

        // Objective   : create a object value from a type __ helperfunction to ParseObject
        // Input       : type
        // Output      : initalized object
        // Description : Bool is set to true, integer to 0 and strings to string.Empty. Bool is true as this later can be checked for existance)
        private object createSystemValue( Type type )
        {
            switch( GetCSAttribType( type ) )
            {
                case AttributeType.Boolean:
                    return true; // this ensures that we don't skip any Specified elements
                case AttributeType.Integer:
                    return 0;

                default:
                    return string.Empty;
            }
        }

        #endregion private_methods
    }
}