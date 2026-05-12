namespace KSR.AST;

public interface IAstVisitor<out T>
{
    T Visit(ProgramNode node);
    T Visit(InterfaceDecl node);
    T Visit(ImplBlock node);
    T Visit(UseDecl node);
    T Visit(StructDecl node);
    T Visit(SealedDecl node);
    T Visit(FunctionDecl node);
    T Visit(ExtFunctionDecl node);
    T Visit(Block node);

    // Statements
    T Visit(ValDecl node);
    T Visit(VarDecl node);
    T Visit(AssignStmt node);
    T Visit(CompoundAssignStmt node);
    T Visit(IndexAssignStmt node);
    T Visit(ReturnStmt node);
    T Visit(IfStmt node);
    T Visit(WhileStmt node);
    T Visit(ForInStmt node);
    T Visit(ExprStmt node);

    // Expressions
    T Visit(IntLiteral node);
    T Visit(DoubleLiteral node);
    T Visit(StringLiteral node);
    T Visit(BoolLiteral node);
    T Visit(NullLiteral node);
    T Visit(StringTemplateExpr node);
    T Visit(ThisExpr node);
    T Visit(IdentifierExpr node);
    T Visit(CallExpr node);
    T Visit(MemberAccessExpr node);
    T Visit(SafeCallExpr node);
    T Visit(ElvisExpr node);
    T Visit(BinaryExpr node);
    T Visit(IndexExpr node);
    T Visit(NewArrayExpr node);
    T Visit(LambdaExpr node);
    T Visit(NewObjectExpr node);
    T Visit(UnaryExpr node);
    T Visit(RangeExpr node);
    T Visit(ListLiteralExpr node);
    T Visit(WhenExpr node);
    T Visit(MapLiteralExpr node);
    T Visit(AwaitExpr node);
    T Visit(NamedArgExpr node);
    T Visit(IsPatternExpr node);
}
