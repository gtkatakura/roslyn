﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.CSharp.Symbols
{
    /// <summary>
    /// A synthesized concept instance method that, when generated,
    /// calls into a corresponding method on a concept default struct.
    /// </summary>
    internal sealed class SynthesizedDefaultStructImplementationMethod : SynthesizedImplementationForwardingMethod
    {
        /// <summary>
        /// The default struct into which this method will call.
        /// </summary>
        private NamedTypeSymbol _defaultStruct;

        public SynthesizedDefaultStructImplementationMethod(MethodSymbol conceptMethod, NamedTypeSymbol defaultStruct, NamedTypeSymbol implementingType)
            : base(conceptMethod, conceptMethod, implementingType)
        {
            _defaultStruct = defaultStruct;
        }

        public override Accessibility DeclaredAccessibility => Accessibility.Public;
        public override MethodKind MethodKind => ImplementingMethod.MethodKind;

        // @t-mawind TODO: should this be an explicit implementation?
        //   if it is, we need to figure out how to make it visible to the
        //   binder.
        internal override bool IsExplicitInterfaceImplementation => false;
        public override ImmutableArray<MethodSymbol> ExplicitInterfaceImplementations => ImmutableArray<MethodSymbol>.Empty;

        public override string Name => ImplementingMethod.Name;
        public override string MetadataName => ImplementingMethod.MetadataName;

        internal override void GenerateMethodBody(TypeCompilationState compilationState, DiagnosticBag diagnostics)
        {
            var concept = ImplementingMethod.ContainingType;
            var conceptLoc = concept.Locations.IsEmpty ? Location.None : concept.Locations[0];
            // TODO: wrong location?

            Debug.Assert(concept.IsConcept, "Tried to synthesise default struct implementation on a non-concept interface");
            
            var instance = ContainingType;
            var instanceLoc = instance.Locations.IsEmpty ? Location.None : instance.Locations[0];
            // TODO: wrong location?

            Debug.Assert(instance.IsInstance, "Tried to synthesise default struct implementation for a non-instance");

            SyntheticBoundNodeFactory F = new SyntheticBoundNodeFactory(this, this.GetNonNullSyntaxNode(), compilationState, diagnostics);
            F.CurrentMethod = OriginalDefinition;

            try
            {

                // Now try to find the default struct using the instance's scope...
                var binder = new BinderFactory(compilationState.Compilation, instance.GetNonNullSyntaxNode().SyntaxTree).GetBinder(instance.GetNonNullSyntaxNode());

                Debug.Assert(_defaultStruct.Arity == 1, "should have already pre-checked default struct arity");
                Debug.Assert(_defaultStruct.TypeParameters[0].IsConceptWitness, "should have already pre-checked default struct witness parameter");

                // Now make the receiver for the call.
                // The receiver has one argument, namely the calling witness.
                // We generate an empty local for it, and then call into that local.
                // We then place the local into the block.
                var recvType = _defaultStruct.Construct(ImmutableArray.Create<TypeSymbol>(instance));
                var recvLocal = F.SynthesizedLocal(recvType, syntax: F.Syntax, kind: SynthesizedLocalKind.ConceptDictionary);
                var receiver = F.Local(recvLocal);

                var arguments = GenerateInnerCallArguments(F);
                Debug.Assert(arguments.Length == ImplementingMethod.Parameters.Length,
                    "Conversion from parameters to arguments lost or gained some entries.");

                var call = F.MakeInvocationExpression(BinderFlags.None, F.Syntax, receiver, ImplementingMethod.Name, arguments, diagnostics, ImplementingMethod.TypeArguments, allowInvokingSpecialMethod: true);
                if (call.HasErrors)
                {
                    F.CloseMethod(F.ThrowNull());
                    return;
                }

                // If whichever call we end up making returns void, then we
                // can't just return its result; instead, we have to do the
                // call on its own _then_ return.
                BoundBlock block;
                if (call.Type.SpecialType == SpecialType.System_Void)
                {
                    block = F.Block(ImmutableArray.Create(receiver.LocalSymbol), F.ExpressionStatement(call), F.Return());
                }
                else
                {
                    block = F.Block(ImmutableArray.Create(receiver.LocalSymbol), F.Return(call));
                }

                F.CloseMethod(block);
            }
            catch (SyntheticBoundNodeFactory.MissingPredefinedMember ex)
            {
                diagnostics.Add(ex.Diagnostic);
                F.CloseMethod(F.ThrowNull());
            }
        }

        /// <summary>
        /// Converts the formal parameters of this method into the
        /// arguments of the inner call.
        /// </summary>
        /// <param name="f">
        /// The factory used to generate the arguments.
        /// </param>
        /// <returns>
        /// A list of bound inner-call arguments.
        /// </returns>
        private ImmutableArray<BoundExpression> GenerateInnerCallArguments(SyntheticBoundNodeFactory f)
        {
            var argumentsB = ArrayBuilder<BoundExpression>.GetInstance();
            foreach (var p in ImplementingMethod.Parameters) argumentsB.Add(f.Parameter(p));
            return argumentsB.ToImmutableAndFree();
        }
    }
}
